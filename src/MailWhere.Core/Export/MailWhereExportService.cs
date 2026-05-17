using System.Text.Encodings.Web;
using System.Text.Json;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Export;

public sealed class MailWhereExportService
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly IFollowUpStore _store;

    public MailWhereExportService(IFollowUpStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<MailWhereExportSnapshot> BuildSnapshotAsync(
        DateTimeOffset generatedAt,
        int archivedLimit = 100,
        CancellationToken cancellationToken = default)
    {
        var openTasks = await _store.ListOpenTasksAsync(cancellationToken).ConfigureAwait(false);
        var archivedTasks = await _store.ListArchivedTasksAsync(archivedLimit, cancellationToken).ConfigureAwait(false);
        var reviewItems = await _store.ListReviewCandidatesAsync(cancellationToken).ConfigureAwait(false);
        var replyProgress = await _store.ListReplyProgressAsync(cancellationToken).ConfigureAwait(false);

        return new MailWhereExportSnapshot(
            CurrentSchemaVersion,
            generatedAt,
            openTasks.Select(MailWhereExportTask.FromTask).ToArray(),
            archivedTasks.Select(MailWhereExportTask.FromTask).ToArray(),
            reviewItems.Select(MailWhereExportReviewCandidate.FromCandidate).ToArray(),
            replyProgress.Select(MailWhereExportReplyProgress.FromProgress).ToArray());
    }

    public static string ToJson(MailWhereExportSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
