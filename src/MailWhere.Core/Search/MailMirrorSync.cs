using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;

namespace MailWhere.Core.Search;

public sealed record MailInventoryRequest(MailSourceFolder Folder, int PageSize = 200, string? Checkpoint = null);

public sealed record MailInventoryItem(
    string StoreId,
    string EntryId,
    MailSourceFolder Folder,
    DateTimeOffset LastModifiedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string Subject,
    string SenderDisplay,
    string? ConversationId = null,
    IReadOnlyList<string>? RecipientDisplayNames = null)
{
    public MailMirrorLocator Locator => new(StoreId, EntryId);
    public string Cursor => MailMirrorCursor.Format(LastModifiedAt, StoreId, EntryId);
}

public sealed record MailInventoryPage(MailSourceFolder Folder, IReadOnlyList<MailInventoryItem> Items, string? NextCheckpoint, bool Completed, IReadOnlyList<MailMirrorSyncWarning>? Warnings = null);

public sealed record MailMirrorSyncWarning(string Code, CapabilitySeverity Severity, string SanitizedErrorClass);
public sealed record MailMirrorSyncProgress(string Folder, int SeenCount, int HydratedCount, string Message);
public sealed record MailMirrorSyncSummary(int SeenCount, int HydratedCount, int SkippedUnchangedCount, IReadOnlyList<MailMirrorSyncWarning> Warnings);

public enum MailMirrorSyncCadence
{
    Initial,
    Incremental,
    Authoritative
}

public static class MailMirrorSyncCadencePolicy
{
    public const string InitialSyncCompletedAtStateKey = "mail-mirror-initial-sync-completed-at";
    public const string LastAuthoritativeReconcileAtStateKey = "mail-mirror-last-authoritative-reconcile-at";
    public static readonly TimeSpan AuthoritativeReconcileInterval = TimeSpan.FromHours(24);

    public static MailMirrorSyncCadence Select(
        DateTimeOffset now,
        bool manualRequested,
        string? initialSyncCompletedAt,
        string? lastAuthoritativeReconcileAt)
    {
        if (!TryParseStateTimestamp(initialSyncCompletedAt, out _))
        {
            return MailMirrorSyncCadence.Initial;
        }

        if (manualRequested || !TryParseStateTimestamp(lastAuthoritativeReconcileAt, out var lastReconcileAt))
        {
            return MailMirrorSyncCadence.Authoritative;
        }

        return lastReconcileAt > now || now - lastReconcileAt >= AuthoritativeReconcileInterval
            ? MailMirrorSyncCadence.Authoritative
            : MailMirrorSyncCadence.Incremental;
    }

    public static bool IsWarningFree(MailMirrorSyncSummary summary) => summary.Warnings.Count == 0;

    private static bool TryParseStateTimestamp(string? value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParse(value, out timestamp);
}

public static class MailInventoryOrdering
{
    public static IOrderedEnumerable<MailInventoryItem> ByCheckpointCursor(IEnumerable<MailInventoryItem> items) =>
        items
            .OrderBy(item => item.LastModifiedAt)
            .ThenBy(item => item.StoreId, StringComparer.Ordinal)
            .ThenBy(item => item.EntryId, StringComparer.Ordinal);

    public static IReadOnlyList<MailInventoryPage> BuildPages(
        MailInventoryRequest request,
        IEnumerable<MailInventoryItem> items,
        IReadOnlyList<MailMirrorSyncWarning>? warnings = null)
    {
        var pageSize = Math.Max(1, request.PageSize);
        var ordered = ByCheckpointCursor(items)
            .Where(item => request.Checkpoint is null || MailMirrorCursor.IsAfter(request.Checkpoint, item))
            .ToArray();
        if (ordered.Length == 0)
        {
            return [new MailInventoryPage(request.Folder, Array.Empty<MailInventoryItem>(), request.Checkpoint, Completed: true, warnings)];
        }

        var pages = new List<MailInventoryPage>((ordered.Length + pageSize - 1) / pageSize);
        for (var index = 0; index < ordered.Length; index += pageSize)
        {
            var pageItems = ordered.Skip(index).Take(pageSize).ToArray();
            var completed = index + pageItems.Length == ordered.Length;
            pages.Add(new MailInventoryPage(
                request.Folder,
                pageItems,
                pageItems[^1].Cursor,
                completed,
                completed ? warnings : null));
        }

        return pages;
    }
}

public interface IMailMirrorInventorySource
{
    IAsyncEnumerable<MailInventoryPage> EnumerateAsync(MailInventoryRequest request, CancellationToken cancellationToken = default);
    Task<MailMirrorMessage?> HydrateAsync(MailInventoryItem item, CancellationToken cancellationToken = default);
}

public sealed class MailMirrorBackfillService
{
    public const int DefaultPageSize = 200;
    public const int DefaultBatchSize = 25;
    private static readonly MailSourceFolder[] DefaultFolders = [MailSourceFolder.Inbox, MailSourceFolder.Sent, MailSourceFolder.Other];
    private readonly IMailMirrorInventorySource _source;
    private readonly IMailMirrorStore _store;

    public MailMirrorBackfillService(IMailMirrorInventorySource source, IMailMirrorStore store)
    {
        _source = source;
        _store = store;
    }

    public async Task<MailMirrorSyncSummary> RunAuthoritativeReconcileAsync(
        IProgress<MailMirrorSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var seen = 0;
        var hydrated = 0;
        var unchanged = 0;
        var warnings = new List<MailMirrorSyncWarning>();

        foreach (var folder in DefaultFolders)
        {
            var currentLocators = new List<MailMirrorLocator>();
            var folderCompleted = false;
            var folderHadInventoryWarning = false;
            await foreach (var page in _source.EnumerateAsync(new MailInventoryRequest(folder, DefaultPageSize), cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page.Warnings is not null && page.Warnings.Count > 0)
                {
                    folderHadInventoryWarning = true;
                    warnings.AddRange(page.Warnings);
                }

                var items = MailInventoryOrdering.ByCheckpointCursor(page.Items).ToArray();
                currentLocators.AddRange(items.Select(item => item.Locator));
                seen += items.Length;
                if (page.Completed)
                {
                    folderCompleted = true;
                }

                var known = await _store.GetKnownLastModifiedAsync(items.Select(item => item.Locator).ToArray(), cancellationToken).ConfigureAwait(false);
                var batch = new List<MailMirrorMessage>(DefaultBatchSize);
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (known.TryGetValue(item.Locator, out var lastModified) && lastModified == item.LastModifiedAt)
                    {
                        unchanged++;
                        continue;
                    }

                    try
                    {
                        var message = await _source.HydrateAsync(item, cancellationToken).ConfigureAwait(false);
                        if (message is null)
                        {
                            warnings.Add(new MailMirrorSyncWarning("mail-hydration-missing", CapabilitySeverity.Degraded, "MissingItem"));
                            continue;
                        }

                        batch.Add(message);
                        hydrated++;
                        if (batch.Count == DefaultBatchSize)
                        {
                            await FlushAsync(batch, item, item.Folder, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(new MailMirrorSyncWarning("mail-hydration-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
                    }
                }

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, items.Length == 0 ? null : items[^1], folder, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new MailMirrorSyncProgress(folder.ToString(), seen, hydrated, "inventory-page-processed"));
            }

            if (folderCompleted && !folderHadInventoryWarning)
            {
                await _store.ReconcileFolderAsync(folder.ToString(), currentLocators, cancellationToken).ConfigureAwait(false);
            }
        }

        return new MailMirrorSyncSummary(seen, hydrated, unchanged, warnings);
    }

    public async Task<MailMirrorSyncSummary> RunInitialBackfillAsync(
        IProgress<MailMirrorSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var seen = 0;
        var hydrated = 0;
        var unchanged = 0;
        var warnings = new List<MailMirrorSyncWarning>();

        foreach (var folder in DefaultFolders)
        {
            var checkpoint = await _store.GetCheckpointAsync(folder.ToString(), cancellationToken).ConfigureAwait(false);
            await foreach (var page in _source.EnumerateAsync(new MailInventoryRequest(folder, DefaultPageSize, checkpoint), cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page.Warnings is not null)
                {
                    warnings.AddRange(page.Warnings);
                }

                var items = MailInventoryOrdering.ByCheckpointCursor(page.Items).ToArray();
                seen += items.Length;

                var known = await _store.GetKnownLastModifiedAsync(items.Select(item => item.Locator).ToArray(), cancellationToken).ConfigureAwait(false);
                var batch = new List<MailMirrorMessage>(DefaultBatchSize);
                MailInventoryItem? checkpointItem = null;
                var checkpointBlockedByFailure = false;
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (known.TryGetValue(item.Locator, out var lastModified) && lastModified == item.LastModifiedAt)
                    {
                        unchanged++;
                        if (!checkpointBlockedByFailure)
                        {
                            checkpointItem = item;
                        }

                        continue;
                    }

                    try
                    {
                        var message = await _source.HydrateAsync(item, cancellationToken).ConfigureAwait(false);
                        if (message is null)
                        {
                            warnings.Add(new MailMirrorSyncWarning("mail-hydration-missing", CapabilitySeverity.Degraded, "MissingItem"));
                            continue;
                        }

                        batch.Add(message);
                        hydrated++;
                        if (!checkpointBlockedByFailure)
                        {
                            checkpointItem = item;
                        }

                        if (batch.Count == DefaultBatchSize)
                        {
                            await FlushAsync(batch, checkpointItem, item.Folder, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        checkpointBlockedByFailure = true;
                        warnings.Add(new MailMirrorSyncWarning("mail-hydration-failed", CapabilitySeverity.Degraded, ex.GetType().Name));
                    }
                }

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, checkpointItem, folder, cancellationToken).ConfigureAwait(false);
                }
                else if (checkpointItem is not null)
                {
                    await _store.UpsertBatchAsync(Array.Empty<MailMirrorMessage>(), new MailMirrorCheckpoint(folder.ToString(), checkpointItem.Cursor), cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new MailMirrorSyncProgress(folder.ToString(), seen, hydrated, "inventory-page-processed"));
            }
        }

        return new MailMirrorSyncSummary(seen, hydrated, unchanged, warnings);
    }

    private Task FlushAsync(List<MailMirrorMessage> batch, MailInventoryItem? checkpointItem, MailSourceFolder folder, CancellationToken cancellationToken)
    {
        var checkpoint = checkpointItem is null ? null : new MailMirrorCheckpoint(folder.ToString(), checkpointItem.Cursor);
        var rows = batch.ToArray();
        batch.Clear();
        return _store.UpsertBatchAsync(rows, checkpoint, cancellationToken);
    }
}

public sealed class MailMirrorEventHintQueue
{
    private int _pending;

    public void NotifyNewMailHint() => Interlocked.Exchange(ref _pending, 1);

    public bool ConsumePendingHint() => Interlocked.Exchange(ref _pending, 0) == 1;
}

public static class MailMirrorCursor
{
    public static string Format(DateTimeOffset lastModifiedAt, string storeId, string entryId) =>
        string.Join("|", lastModifiedAt.ToString("O"), Escape(storeId), Escape(entryId));

    public static bool IsAfter(string cursor, MailInventoryItem item)
    {
        var parts = cursor.Split('|');
        if (parts.Length != 3 || !DateTimeOffset.TryParse(parts[0], out var timestamp))
        {
            return true;
        }

        var compare = item.LastModifiedAt.CompareTo(timestamp);
        if (compare != 0)
        {
            return compare > 0;
        }

        compare = string.CompareOrdinal(item.StoreId, Unescape(parts[1]));
        if (compare != 0)
        {
            return compare > 0;
        }

        return string.CompareOrdinal(item.EntryId, Unescape(parts[2])) > 0;
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace("|", "%7C");
    private static string Unescape(string value) => value.Replace("%7C", "|").Replace("%25", "%");
}
