using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;
using MailWhere.Core.Mail;

namespace MailWhere.OutlookCom;

public sealed class OutlookComMailSource : IEmailSource
{
    public Task<EmailReadResult> ReadRecentAsync(int maxItems, bool includeBody, CancellationToken cancellationToken = default) =>
        ReadAsync(new MailReadRequest(maxItems, includeBody), cancellationToken);

    public Task<EmailReadResult> ReadAsync(MailReadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OutlookStaExecutor.RunAsync(() => ReadRecentInboxMessages(request, cancellationToken), cancellationToken);
    }

    public EmailReadResult ReadRecentInboxMessages(int maxItems, bool includeBody, CancellationToken cancellationToken = default) =>
        ReadRecentInboxMessages(new MailReadRequest(maxItems, includeBody), cancellationToken);

    public EmailReadResult ReadRecentInboxMessages(MailReadRequest request, CancellationToken cancellationToken = default)
    {
        object? outlook = null;
        object? session = null;
        object? inbox = null;
        var warnings = new List<MailReadWarning>();
        var limit = request.MaxItems <= 0 ? int.MaxValue : request.MaxItems;
        var messages = new List<EmailSnapshot>(Math.Min(limit, 512));
        var skipped = 0;

        try
        {
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                return EmailReadResult.Empty(new MailReadWarning("outlook-progid-unavailable", CapabilitySeverity.Blocked, "ProgIdUnavailable"));
            }

            outlook = Activator.CreateInstance(outlookType);
            if (outlook is null)
            {
                return EmailReadResult.Empty(new MailReadWarning("outlook-com-null", CapabilitySeverity.Blocked, "CreateInstanceReturnedNull"));
            }

            dynamic outlookDynamic = outlook;
            session = outlookDynamic.Session;
            dynamic sessionDynamic = session;
            var mailboxIdentity = TryReadCurrentUserIdentity(session);
            inbox = sessionDynamic.GetDefaultFolder(6); // olFolderInbox
            var inboxMessages = ReadFolderMessages(
                inbox,
                "inbox",
                "ReceivedTime",
                request,
                mailboxIdentity,
                cancellationToken,
                warnings,
                ref skipped);

            var sentMessages = ReadSentMessages((object)session, request, mailboxIdentity, cancellationToken, warnings, ref skipped);
            messages.AddRange(inboxMessages.Concat(sentMessages)
                .OrderByDescending(message => message.ReceivedAt)
                .Take(limit));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new MailReadWarning("outlook-read-failed", CapabilitySeverity.Blocked, ex.GetType().Name));
        }
        finally
        {
            ComRelease.FinalRelease(inbox);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }

        return new EmailReadResult(messages, warnings, skipped);
    }

    private static IReadOnlyList<EmailSnapshot> ReadSentMessages(
        object session,
        MailReadRequest request,
        MailboxIdentity mailboxIdentity,
        CancellationToken cancellationToken,
        List<MailReadWarning> warnings,
        ref int skipped)
    {
        object? sent = null;
        try
        {
            sent = session.GetType().InvokeMember(
                "GetDefaultFolder",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                session,
                new object[] { 5 }); // olFolderSentMail
            if (sent is null)
            {
                warnings.Add(new MailReadWarning("outlook-sent-folder-unavailable", CapabilitySeverity.Degraded, "NullFolder"));
                return Array.Empty<EmailSnapshot>();
            }

            return ReadFolderMessages(
                sent,
                "sent",
                "SentOn",
                request,
                mailboxIdentity,
                cancellationToken,
                warnings,
                ref skipped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new MailReadWarning("outlook-sent-folder-unavailable", CapabilitySeverity.Degraded, ex.GetType().Name));
            return Array.Empty<EmailSnapshot>();
        }
        finally
        {
            ComRelease.FinalRelease(sent);
        }
    }

    private static IReadOnlyList<EmailSnapshot> ReadFolderMessages(
        object folder,
        string folderCode,
        string datePropertyName,
        MailReadRequest request,
        MailboxIdentity mailboxIdentity,
        CancellationToken cancellationToken,
        List<MailReadWarning> warnings,
        ref int skipped)
    {
        object? items = null;
        var limit = request.MaxItems <= 0 ? int.MaxValue : request.MaxItems;
        var messages = new List<EmailSnapshot>(Math.Min(limit, 512));
        try
        {
            dynamic folderDynamic = folder;
            items = folderDynamic.Items;
            ((dynamic)items).Sort($"[{datePropertyName}]", true);

            int total = Convert.ToInt32(((dynamic)items).Count);
            for (var i = 1; i <= total && messages.Count < limit; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = ((dynamic)items)[i];
                    dynamic itemDynamic = item;
                    var itemDate = TryReadDateTime(item, datePropertyName)
                                   ?? TryReadDateTime(item, "ReceivedTime")
                                   ?? TryReadDateTime(item, "SentOn")
                                   ?? DateTime.Now;
                    var receivedAt = new DateTimeOffset(itemDate);
                    if (request.Since is not null && receivedAt < request.Since.Value)
                    {
                        break;
                    }

                    string entryId = Convert.ToString(itemDynamic.EntryID) ?? $"unknown-{folderCode}-{i}";
                    string subject = Convert.ToString(itemDynamic.Subject) ?? string.Empty;
                    string sender = Convert.ToString(itemDynamic.SenderName) ?? string.Empty;
                    string? body = request.IncludeBody ? Convert.ToString(itemDynamic.Body) : null;
                    string? conversationId = TryReadString(item, "ConversationID");
                    var recipients = SplitRecipients(TryReadString(item, "To"), TryReadString(item, "CC"));
                    var recipientRole = folderCode == "sent"
                        ? MailboxRecipientRole.Other
                        : TryResolveMailboxRecipientRole(item, mailboxIdentity);

                    messages.Add(new EmailSnapshot(
                        entryId,
                        receivedAt,
                        sender,
                        subject,
                        body,
                        conversationId,
                        mailboxIdentity.DisplayName,
                        recipients,
                        recipientRole));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    skipped++;
                    warnings.Add(new MailReadWarning($"outlook-{folderCode}-item-read-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
                }
                finally
                {
                    ComRelease.FinalRelease(item);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new MailReadWarning($"outlook-{folderCode}-read-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
        }
        finally
        {
            ComRelease.FinalRelease(items);
        }

        return messages;
    }

    private static DateTime? TryReadDateTime(object item, string propertyName)
    {
        try
        {
            var value = item.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, item, null);
            return value is DateTime date ? date : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadString(object item, string propertyName)
    {
        try
        {
            var value = item.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, item, null);
            return Convert.ToString(value);
        }
        catch
        {
            return null;
        }
    }

    private static MailboxIdentity TryReadCurrentUserIdentity(object session)
    {
        object? currentUser = null;
        try
        {
            currentUser = session.GetType().InvokeMember("CurrentUser", System.Reflection.BindingFlags.GetProperty, null, session, null);
            if (currentUser is null)
            {
                return MailboxIdentity.Empty;
            }

            var displayName = TryReadString(currentUser, "Name")
                              ?? TryReadString(currentUser, "Address")
                              ?? Convert.ToString(currentUser);
            var address = TryReadString(currentUser, "Address");
            var smtpAddress = TryReadSmtpAddress(currentUser);
            return new MailboxIdentity(displayName, BuildAliases(displayName, address, smtpAddress));
        }
        catch
        {
            return MailboxIdentity.Empty;
        }
        finally
        {
            ComRelease.FinalRelease(currentUser);
        }
    }

    private static string? TryReadSmtpAddress(object addressEntry)
    {
        object? exchangeUser = null;
        try
        {
            exchangeUser = addressEntry.GetType().InvokeMember("GetExchangeUser", System.Reflection.BindingFlags.InvokeMethod, null, addressEntry, null);
            if (exchangeUser is null)
            {
                return null;
            }

            return TryReadString(exchangeUser, "PrimarySmtpAddress")
                   ?? TryReadString(exchangeUser, "Address");
        }
        catch
        {
            return null;
        }
        finally
        {
            ComRelease.FinalRelease(exchangeUser);
        }
    }

    private static MailboxRecipientRole TryResolveMailboxRecipientRole(object item, MailboxIdentity mailboxIdentity)
    {
        object? recipients = null;
        try
        {
            recipients = item.GetType().InvokeMember("Recipients", System.Reflection.BindingFlags.GetProperty, null, item, null);
            if (recipients is null)
            {
                return MailboxRecipientRole.Other;
            }

            dynamic recipientsDynamic = recipients;
            int count = Convert.ToInt32(recipientsDynamic.Count);
            var sawCc = false;
            var sawBcc = false;
            for (var i = 1; i <= count; i++)
            {
                object? recipient = null;
                try
                {
                    recipient = recipientsDynamic[i];
                    dynamic recipientDynamic = recipient;
                    var type = Convert.ToInt32(recipientDynamic.Type);
                    if (!RecipientLooksLikeMailboxUser(recipient, mailboxIdentity))
                    {
                        continue;
                    }

                    if (type == 1)
                    {
                        return MailboxRecipientRole.Direct;
                    }

                    if (type == 2)
                    {
                        sawCc = true;
                    }
                    else if (type == 3)
                    {
                        sawBcc = true;
                    }
                }
                catch
                {
                    // Keep recipient-role uncertainty conservative; do not promote it to Direct.
                }
                finally
                {
                    ComRelease.FinalRelease(recipient);
                }
            }

            if (sawCc)
            {
                return MailboxRecipientRole.Cc;
            }

            return sawBcc ? MailboxRecipientRole.Bcc : MailboxRecipientRole.Other;
        }
        catch
        {
            return MailboxRecipientRole.Other;
        }
        finally
        {
            ComRelease.FinalRelease(recipients);
        }
    }

    private static bool RecipientLooksLikeMailboxUser(object recipient, MailboxIdentity mailboxIdentity)
    {
        if (mailboxIdentity.Aliases.Count == 0)
        {
            return false;
        }

        var candidates = new[]
            {
                TryReadString(recipient, "Name"),
                TryReadString(recipient, "Address")
            }
            .Concat(TryReadRecipientSmtpAddresses(recipient))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeIdentity(value!))
            .ToArray();

        return candidates.Any(candidate => mailboxIdentity.Aliases.Contains(candidate));
    }

    private static IEnumerable<string?> TryReadRecipientSmtpAddresses(object recipient)
    {
        object? addressEntry = null;
        try
        {
            addressEntry = recipient.GetType().InvokeMember("AddressEntry", System.Reflection.BindingFlags.GetProperty, null, recipient, null);
            if (addressEntry is null)
            {
                yield break;
            }

            yield return TryReadString(addressEntry, "Name");
            yield return TryReadString(addressEntry, "Address");
            yield return TryReadSmtpAddress(addressEntry);
        }
        finally
        {
            ComRelease.FinalRelease(addressEntry);
        }
    }

    private static HashSet<string> BuildAliases(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeIdentity(value!))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeIdentity(string value) =>
        value.Trim().Trim('"').Trim('<', '>').ToLowerInvariant();

    private static IReadOnlyList<string> SplitRecipients(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record MailboxIdentity(string? DisplayName, HashSet<string> Aliases)
    {
        public static MailboxIdentity Empty { get; } = new(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
