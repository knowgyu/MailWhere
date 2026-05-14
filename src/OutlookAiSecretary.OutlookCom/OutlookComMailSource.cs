using OutlookAiSecretary.Core.Capabilities;
using OutlookAiSecretary.Core.Domain;
using OutlookAiSecretary.Core.Mail;

namespace OutlookAiSecretary.OutlookCom;

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
        if (request.MaxItems <= 0)
        {
            return new EmailReadResult(Array.Empty<EmailSnapshot>(), Array.Empty<MailReadWarning>(), 0);
        }

        object? outlook = null;
        object? session = null;
        object? inbox = null;
        object? items = null;
        var warnings = new List<MailReadWarning>();
        var messages = new List<EmailSnapshot>(request.MaxItems);
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
            inbox = sessionDynamic.GetDefaultFolder(6); // olFolderInbox
            dynamic inboxDynamic = inbox;
            items = inboxDynamic.Items;
            ((dynamic)items).Sort("[ReceivedTime]", true);

            int total = Convert.ToInt32(((dynamic)items).Count);
            for (var i = 1; i <= total && messages.Count < request.MaxItems; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = ((dynamic)items)[i];
                    dynamic itemDynamic = item;
                    DateTime received = itemDynamic.ReceivedTime;
                    var receivedAt = new DateTimeOffset(received);
                    if (request.Since is not null && receivedAt < request.Since.Value)
                    {
                        break;
                    }

                    string entryId = Convert.ToString(itemDynamic.EntryID) ?? $"unknown-{i}";
                    string subject = Convert.ToString(itemDynamic.Subject) ?? string.Empty;
                    string sender = Convert.ToString(itemDynamic.SenderName) ?? string.Empty;
                    string? body = request.IncludeBody ? Convert.ToString(itemDynamic.Body) : null;
                    string? conversationId = TryReadString(item, "ConversationID");

                    messages.Add(new EmailSnapshot(
                        entryId,
                        receivedAt,
                        sender,
                        subject,
                        body,
                        conversationId));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    skipped++;
                    warnings.Add(new MailReadWarning("outlook-item-read-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
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
            warnings.Add(new MailReadWarning("outlook-read-failed", CapabilitySeverity.Blocked, ex.GetType().Name));
        }
        finally
        {
            ComRelease.FinalRelease(items);
            ComRelease.FinalRelease(inbox);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }

        return new EmailReadResult(messages, warnings, skipped);
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
}
