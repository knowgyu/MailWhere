using System.Reflection;
using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;
using MailWhere.Core.Search;

namespace MailWhere.OutlookCom;

public sealed class OutlookComMailInventorySource : IMailMirrorInventorySource
{
    public async IAsyncEnumerable<MailInventoryPage> EnumerateAsync(
        MailInventoryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pages = await OutlookStaExecutor.RunAsync(() => ReadFolderPages(request, cancellationToken), cancellationToken).ConfigureAwait(false);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return page;
        }
    }

    public Task<MailMirrorMessage?> HydrateAsync(MailInventoryItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OutlookStaExecutor.RunAsync(() => HydrateOnSta(item, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<MailInventoryPage> ReadFolderPages(MailInventoryRequest request, CancellationToken cancellationToken)
    {
        object? outlook = null;
        object? session = null;
        object? folder = null;
        object? table = null;
        var pages = new List<MailInventoryPage>();
        var warnings = new List<MailMirrorSyncWarning>();

        try
        {
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                return [WarningPage(request.Folder, "outlook-com-unavailable", "ProgIdUnavailable")];
            }

            outlook = Activator.CreateInstance(outlookType);
            if (outlook is null)
            {
                return [WarningPage(request.Folder, "outlook-com-unavailable", "CreateInstanceReturnedNull")];
            }

            session = Get(outlook, "Session") ?? throw new InvalidOperationException("OutlookSessionUnavailable");
            folder = Invoke(session, "GetDefaultFolder", request.Folder == MailSourceFolder.Sent ? 5 : 6)
                ?? throw new InvalidOperationException("OutlookFolderUnavailable");
            var storeId = Convert.ToString(Get(folder, "StoreID")) ?? string.Empty;
            table = Invoke(folder, "GetTable") ?? throw new InvalidOperationException("OutlookTableUnavailable");
            TryAddTableColumns(table);
            TrySort(table, "LastModificationTime");

            var items = new List<MailInventoryItem>(request.PageSize);
            while (HasNext(table))
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? row = null;
                try
                {
                    row = Invoke(table, "GetNextRow") ?? throw new InvalidOperationException("OutlookRowUnavailable");
                    var item = ReadRow(row, storeId, request.Folder);
                    if (request.Checkpoint is not null && !MailMirrorCursor.IsAfter(request.Checkpoint, item))
                    {
                        continue;
                    }

                    items.Add(item);
                    if (items.Count == request.PageSize)
                    {
                        pages.Add(new MailInventoryPage(request.Folder, items.ToArray(), items[^1].Cursor, Completed: false));
                        items.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add(new MailMirrorSyncWarning("outlook-inventory-item-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
                }
                finally
                {
                    ComRelease.FinalRelease(row);
                }
            }

            pages.Add(new MailInventoryPage(request.Folder, items.ToArray(), items.Count == 0 ? request.Checkpoint : items[^1].Cursor, Completed: true, warnings));
            return pages;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [WarningPage(request.Folder, "outlook-inventory-folder-failed", ex.GetType().Name)];
        }
        finally
        {
            ComRelease.FinalRelease(table);
            ComRelease.FinalRelease(folder);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }
    }

    private static MailMirrorMessage? HydrateOnSta(MailInventoryItem item, CancellationToken cancellationToken)
    {
        object? outlook = null;
        object? session = null;
        object? mail = null;
        try
        {
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            outlook = Activator.CreateInstance(outlookType) ?? throw new InvalidOperationException("OutlookCreateFailed");
            session = Get(outlook, "Session") ?? throw new InvalidOperationException("OutlookSessionUnavailable");
            mail = Invoke(session, "GetItemFromID", item.EntryId, item.StoreId) ?? throw new InvalidOperationException("OutlookItemUnavailable");
            var body = Convert.ToString(Get(mail, "Body")) ?? string.Empty;
            return new MailMirrorMessage(
                item.StoreId,
                item.EntryId,
                item.Folder,
                item.LastModifiedAt,
                item.Subject,
                item.SenderDisplay,
                body,
                item.ReceivedAt,
                item.SentAt,
                item.ConversationId,
                item.RecipientDisplayNames);
        }
        finally
        {
            ComRelease.FinalRelease(mail);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }
    }

    private static MailInventoryItem ReadRow(object row, string storeId, MailSourceFolder folder)
    {
        var entryId = Convert.ToString(Get(row, "EntryID")) ?? string.Empty;
        var modified = ReadDate(row, "LastModificationTime") ?? DateTimeOffset.MinValue;
        var received = ReadDate(row, "ReceivedTime");
        var sent = ReadDate(row, "SentOn");
        return new MailInventoryItem(
            storeId,
            entryId,
            folder,
            modified,
            received,
            sent,
            Convert.ToString(Get(row, "Subject")) ?? string.Empty,
            Convert.ToString(Get(row, "SenderName")) ?? string.Empty,
            Convert.ToString(Get(row, "ConversationID")),
            SplitRecipients(Convert.ToString(Get(row, "To")), Convert.ToString(Get(row, "CC"))));
    }

    private static void TryAddTableColumns(object table)
    {
        try
        {
            var columns = Get(table, "Columns") ?? throw new InvalidOperationException("OutlookColumnsUnavailable");
            foreach (var name in new[] { "EntryID", "Subject", "SenderName", "ReceivedTime", "SentOn", "LastModificationTime", "ConversationID", "To", "CC" })
            {
                Invoke(columns, "Add", name);
            }

            ComRelease.FinalRelease(columns);
        }
        catch
        {
            // Default columns are enough on some Outlook builds.
        }
    }

    private static void TrySort(object table, string columnName)
    {
        try
        {
            Invoke(table, "Sort", columnName, false);
        }
        catch
        {
            // Service re-sorts each page deterministically before hydration.
        }
    }

    private static bool HasNext(object table) => Convert.ToBoolean(Get(table, "EndOfTable")) == false;

    private static DateTimeOffset? ReadDate(object row, string name)
    {
        var value = Get(row, name);
        return value is DateTime date ? new DateTimeOffset(date) : null;
    }

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static MailInventoryPage WarningPage(MailSourceFolder folder, string code, string errorClass) =>
        new(folder, Array.Empty<MailInventoryItem>(), null, Completed: true, [new MailMirrorSyncWarning(code, CapabilitySeverity.Degraded, errorClass)]);

    private static IReadOnlyList<string> SplitRecipients(params string?[] values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .SelectMany(value => value!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
