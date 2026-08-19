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
        object? store = null;
        object? rootFolder = null;
        var warnings = new List<MailMirrorSyncWarning>();
        var items = new List<MailInventoryItem>();

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
            if (request.Folder == MailSourceFolder.Other)
            {
                store = Get(session, "DefaultStore") ?? throw new InvalidOperationException("OutlookDefaultStoreUnavailable");
                rootFolder = Invoke(store, "GetRootFolder") ?? throw new InvalidOperationException("OutlookRootFolderUnavailable");
                var defaultFolderIds = new HashSet<string>(StringComparer.Ordinal)
                {
                    ReadDefaultFolderEntryId(session, 5),
                    ReadDefaultFolderEntryId(session, 6)
                };
                ReadChildMailFolders(rootFolder, defaultFolderIds, ReadSearchFolderIds(store), items, warnings, cancellationToken);
            }
            else
            {
                folder = Invoke(session, "GetDefaultFolder", request.Folder == MailSourceFolder.Sent ? 5 : 6)
                    ?? throw new InvalidOperationException("OutlookFolderUnavailable");
                ReadFolderItems(folder, request.Folder, items, warnings, cancellationToken);
            }

            return MailInventoryOrdering.BuildPages(request, items, warnings);
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
            ComRelease.FinalRelease(rootFolder);
            ComRelease.FinalRelease(store);
            ComRelease.FinalRelease(folder);
            ComRelease.FinalRelease(session);
            ComRelease.FinalRelease(outlook);
        }
    }

    private static void ReadChildMailFolders(
        object parent,
        HashSet<string> defaultFolderIds,
        HashSet<string> searchFolderIds,
        List<MailInventoryItem> items,
        List<MailMirrorSyncWarning> warnings,
        CancellationToken cancellationToken)
    {
        object? folders = null;
        try
        {
            folders = Get(parent, "Folders") ?? throw new InvalidOperationException("OutlookFoldersUnavailable");
            var count = Convert.ToInt32(Get(folders, "Count"));
            for (var index = 1; index <= count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? child = null;
                try
                {
                    child = Invoke(folders, "Item", index) ?? throw new InvalidOperationException("OutlookFolderUnavailable");
                    var entryId = Convert.ToString(Get(child, "EntryID")) ?? string.Empty;
                    if (searchFolderIds.Contains(entryId))
                    {
                        continue;
                    }

                    if (!defaultFolderIds.Contains(entryId) && IsMailFolder(child))
                    {
                        ReadFolderItems(child, MailSourceFolder.Other, items, warnings, cancellationToken);
                    }

                    ReadChildMailFolders(child, defaultFolderIds, searchFolderIds, items, warnings, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add(new MailMirrorSyncWarning("outlook-inventory-folder-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
                }
                finally
                {
                    ComRelease.FinalRelease(child);
                }
            }
        }
        finally
        {
            ComRelease.FinalRelease(folders);
        }
    }

    private static void ReadFolderItems(
        object folder,
        MailSourceFolder sourceFolder,
        List<MailInventoryItem> items,
        List<MailMirrorSyncWarning> warnings,
        CancellationToken cancellationToken)
    {
        object? table = null;
        try
        {
            var storeId = Convert.ToString(Get(folder, "StoreID")) ?? string.Empty;
            table = Invoke(folder, "GetTable") ?? throw new InvalidOperationException("OutlookTableUnavailable");
            TryAddTableColumns(table);
            TrySort(table, "LastModificationTime");

            while (HasNext(table))
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? row = null;
                try
                {
                    row = Invoke(table, "GetNextRow") ?? throw new InvalidOperationException("OutlookRowUnavailable");
                    items.Add(ReadRow(row, storeId, sourceFolder));
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new MailMirrorSyncWarning("outlook-inventory-folder-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
        }
        finally
        {
            ComRelease.FinalRelease(table);
        }
    }

    private static string ReadDefaultFolderEntryId(object session, int folderId)
    {
        object? folder = null;
        try
        {
            folder = Invoke(session, "GetDefaultFolder", folderId) ?? throw new InvalidOperationException("OutlookFolderUnavailable");
            return Convert.ToString(Get(folder, "EntryID")) ?? string.Empty;
        }
        finally
        {
            ComRelease.FinalRelease(folder);
        }
    }

    private static HashSet<string> ReadSearchFolderIds(object store)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        object? folders = null;
        try
        {
            folders = Invoke(store, "GetSearchFolders");
            var count = Convert.ToInt32(Get(folders!, "Count"));
            for (var index = 1; index <= count; index++)
            {
                object? folder = null;
                try
                {
                    folder = Invoke(folders!, "Item", index);
                    var entryId = folder is null ? null : Convert.ToString(Get(folder, "EntryID"));
                    if (!string.IsNullOrWhiteSpace(entryId))
                    {
                        result.Add(entryId);
                    }
                }
                finally
                {
                    ComRelease.FinalRelease(folder);
                }
            }
        }
        catch
        {
            // Search folders are virtual duplicates; unsupported Outlook builds simply omit this optimization.
        }
        finally
        {
            ComRelease.FinalRelease(folders);
        }

        return result;
    }

    private static bool IsMailFolder(object folder)
    {
        try
        {
            return Convert.ToInt32(Get(folder, "DefaultItemType")) == 0;
        }
        catch
        {
            return false;
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
