using Microsoft.Data.Sqlite;
using OutlookAiSecretary.Core.Domain;
using OutlookAiSecretary.Core.Storage;

namespace OutlookAiSecretary.Storage;

public sealed class SqliteFollowUpStore : IFollowUpStore
{
    private readonly string _connectionString;

    public SqliteFollowUpStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = Schema.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasProcessedSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM processed_sources WHERE source_id_hash = $source LIMIT 1";
        command.Parameters.AddWithValue("$source", sourceIdHash);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task SaveTaskAsync(LocalTaskItem task, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO tasks
            (id, title, due_at, source_id_hash, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted)
            VALUES ($id, $title, $dueAt, $source, $confidence, $reason, $evidence, $status, $snooze, $created, $updated, $deleted)
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", EvidencePolicy.Truncate(task.Title) ?? LocalTaskItem.RedactedTitle);
        command.Parameters.AddWithValue("$dueAt", (object?)task.DueAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)task.SourceIdHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", task.Confidence);
        command.Parameters.AddWithValue("$reason", EvidencePolicy.Truncate(task.Reason) ?? LocalTaskItem.RedactedReason);
        command.Parameters.AddWithValue("$evidence", (object?)EvidencePolicy.Truncate(task.EvidenceSnippet) ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$snooze", (object?)task.SnoozeUntil?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deleted", task.SourceDerivedDataDeleted ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveReviewCandidateAsync(ReviewCandidate candidate, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO review_candidates
            (id, source_id_hash, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, suppressed)
            VALUES ($id, $source, $kind, $confidence, $title, $reason, $evidence, $dueAt, $created, $suppressed)
            """;
        command.Parameters.AddWithValue("$id", candidate.Id.ToString());
        command.Parameters.AddWithValue("$source", candidate.SourceIdHash);
        command.Parameters.AddWithValue("$kind", candidate.Analysis.Kind.ToString());
        command.Parameters.AddWithValue("$confidence", candidate.Analysis.Confidence);
        command.Parameters.AddWithValue("$title", EvidencePolicy.Truncate(candidate.Analysis.SuggestedTitle) ?? string.Empty);
        command.Parameters.AddWithValue("$reason", EvidencePolicy.Truncate(candidate.Analysis.Reason) ?? "Review candidate");
        command.Parameters.AddWithValue("$evidence", (object?)EvidencePolicy.Truncate(candidate.Analysis.EvidenceSnippet) ?? DBNull.Value);
        command.Parameters.AddWithValue("$dueAt", (object?)candidate.Analysis.DueAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", candidate.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$suppressed", candidate.Suppressed ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO processed_sources (source_id_hash, processed_at) VALUES ($source, $processedAt)";
        command.Parameters.AddWithValue("$source", sourceIdHash);
        command.Parameters.AddWithValue("$processedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, due_at, source_id_hash, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted FROM tasks WHERE status IN ('Open','Snoozed') ORDER BY due_at IS NULL, due_at, created_at";
        var tasks = new List<LocalTaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task MarkNotATaskAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await DeleteSourceDerivedDataForSourceAsync(sourceIdHash, cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE review_candidates SET suppressed = 1 WHERE source_id_hash = $source";
        command.Parameters.AddWithValue("$source", sourceIdHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSourceDerivedDataAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT source_id_hash FROM tasks WHERE id = $id";
        lookup.Parameters.AddWithValue("$id", taskId.ToString());
        var result = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var sourceHash = result is not null && result != DBNull.Value
            ? Convert.ToString(result)
            : null;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_derived_data_deleted = 1, updated_at = $updated WHERE id = $id";
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        command.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(sourceHash))
        {
            await RedactSourceDerivedDataForSourceAsync(connection, transaction, sourceHash, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSourceDerivedDataForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await RedactSourceDerivedDataForSourceAsync(connection, transaction, sourceIdHash, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RedactSourceDerivedDataForSourceAsync(SqliteConnection connection, SqliteTransaction transaction, string sourceIdHash, CancellationToken cancellationToken)
    {
        var taskCommand = connection.CreateCommand();
        taskCommand.Transaction = transaction;
        taskCommand.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_derived_data_deleted = 1, updated_at = $updated WHERE source_id_hash = $source";
        taskCommand.Parameters.AddWithValue("$source", sourceIdHash);
        taskCommand.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        taskCommand.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        taskCommand.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await taskCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var candidateCommand = connection.CreateCommand();
        candidateCommand.Transaction = transaction;
        candidateCommand.CommandText = "UPDATE review_candidates SET suggested_title = $title, reason = $reason, evidence_snippet = NULL WHERE source_id_hash = $source";
        candidateCommand.Parameters.AddWithValue("$source", sourceIdHash);
        candidateCommand.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        candidateCommand.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        await candidateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LocalTaskItem ReadTask(SqliteDataReader reader)
    {
        static DateTimeOffset? MaybeDate(object value) => value == DBNull.Value ? null : DateTimeOffset.Parse((string)value);

        return new LocalTaskItem(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            MaybeDate(reader.GetValue(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetDouble(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            Enum.Parse<LocalTaskStatus>(reader.GetString(7)),
            MaybeDate(reader.GetValue(8)),
            DateTimeOffset.Parse(reader.GetString(9)),
            DateTimeOffset.Parse(reader.GetString(10)),
            reader.GetInt32(11) == 1);
    }
}
