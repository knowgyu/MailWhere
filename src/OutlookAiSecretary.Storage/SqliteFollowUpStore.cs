using Microsoft.Data.Sqlite;
using OutlookAiSecretary.Core.Domain;
using OutlookAiSecretary.Core.Storage;

namespace OutlookAiSecretary.Storage;

public sealed class SqliteFollowUpStore : IFollowUpStore, IAppStateStore
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
        command.CommandText = Schema.TablesSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await EnsureColumnAsync(connection, "review_candidates", "resolved_at", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "review_candidates", "resolution", "TEXT NULL", cancellationToken).ConfigureAwait(false);

        command.CommandText = Schema.IndexesSql;
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
        await SaveTaskAsync(connection, null, task, cancellationToken).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<ReviewCandidate>> ListReviewCandidatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_id_hash, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, suppressed
            FROM review_candidates
            WHERE suppressed = 0 AND resolved_at IS NULL
            ORDER BY created_at DESC
            LIMIT 100
            """;
        var candidates = new List<ReviewCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(ReadCandidate(reader));
        }

        return candidates;
    }

    public async Task<ReviewCandidate?> GetReviewCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadActiveReviewCandidateAsync(connection, null, candidateId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalTaskItem?> ResolveReviewCandidateAsTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var candidate = await ReadActiveReviewCandidateAsync(connection, transaction, candidateId, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var resolve = connection.CreateCommand();
        resolve.Transaction = transaction;
        resolve.CommandText = """
            UPDATE review_candidates
            SET suppressed = 1,
                resolved_at = $resolvedAt,
                resolution = $resolution
            WHERE id = $id AND suppressed = 0 AND resolved_at IS NULL
            """;
        resolve.Parameters.AddWithValue("$id", candidateId.ToString());
        resolve.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
        resolve.Parameters.AddWithValue("$resolution", "TaskCreated");
        var resolvedRows = await resolve.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (resolvedRows == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var title = EvidencePolicy.Truncate(candidate.Analysis.SuggestedTitle) ?? "메일 확인";
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            title,
            candidate.Analysis.DueAt,
            candidate.SourceIdHash,
            Math.Clamp(candidate.Analysis.Confidence, 0, 1),
            EvidencePolicy.Truncate(candidate.Analysis.Reason) ?? "검토 후보에서 등록",
            EvidencePolicy.Truncate(candidate.Analysis.EvidenceSnippet),
            LocalTaskStatus.Open,
            null,
            now,
            now);

        await SaveTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return task;
    }

    public async Task<bool> ResolveReviewCandidateAsNotTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE review_candidates
            SET suggested_title = $title,
                reason = $reason,
                evidence_snippet = NULL,
                suppressed = 1,
                resolved_at = $resolvedAt,
                resolution = $resolution
            WHERE id = $id AND suppressed = 0 AND resolved_at IS NULL
            """;
        update.Parameters.AddWithValue("$id", candidateId.ToString());
        update.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        update.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        update.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
        update.Parameters.AddWithValue("$resolution", "NotATask");
        var rows = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<string?> GetAppStateAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null || result == DBNull.Value ? null : Convert.ToString(result);
    }

    public async Task SetAppStateAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO app_state (key, value, updated_at)
            VALUES ($key, $value, $updatedAt)
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
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

    private static async Task SaveTaskAsync(SqliteConnection connection, SqliteTransaction? transaction, LocalTaskItem task, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
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

    private static async Task<ReviewCandidate?> ReadActiveReviewCandidateAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid candidateId, CancellationToken cancellationToken)
    {
        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT id, source_id_hash, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, suppressed
            FROM review_candidates
            WHERE id = $id AND suppressed = 0 AND resolved_at IS NULL
            LIMIT 1
            """;
        lookup.Parameters.AddWithValue("$id", candidateId.ToString());

        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadCandidate(reader) : null;
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        EnsureSafeIdentifier(table);
        EnsureSafeIdentifier(column);
        var probe = connection.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({table})";
        await using (var reader = await probe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureSafeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException("Unsafe SQLite identifier.");
        }
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

    private static ReviewCandidate ReadCandidate(SqliteDataReader reader)
    {
        static DateTimeOffset? MaybeDate(object value) => value == DBNull.Value ? null : DateTimeOffset.Parse((string)value);

        var analysis = new FollowUpAnalysis(
            Enum.Parse<FollowUpKind>(reader.GetString(2)),
            AnalysisDisposition.Review,
            reader.GetDouble(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            MaybeDate(reader.GetValue(7)));

        return new ReviewCandidate(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            analysis,
            DateTimeOffset.Parse(reader.GetString(8)),
            reader.GetInt32(9) == 1);
    }
}
