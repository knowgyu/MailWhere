using Microsoft.Data.Sqlite;
using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Storage;

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
        await EnsureColumnAsync(connection, "review_candidates", "snooze_until", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "review_candidates", "source_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "review_candidates", "source_sender_display", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "review_candidates", "source_received_at", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "review_candidates", "source_recipient_role", "TEXT NOT NULL DEFAULT 'Direct'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "source_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "source_sender_display", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "source_received_at", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "source_recipient_role", "TEXT NOT NULL DEFAULT 'Direct'", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "kind", "TEXT NOT NULL DEFAULT 'ActionRequested'", cancellationToken).ConfigureAwait(false);

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
            (id, source_id_hash, source_id, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, snooze_until, source_sender_display, source_received_at, source_recipient_role, suppressed)
            VALUES ($id, $source, $sourceId, $kind, $confidence, $title, $reason, $evidence, $dueAt, $created, $snoozeUntil, $sender, $receivedAt, $recipientRole, $suppressed)
            """;
        command.Parameters.AddWithValue("$id", candidate.Id.ToString());
        command.Parameters.AddWithValue("$source", candidate.SourceIdHash);
        command.Parameters.AddWithValue("$sourceId", (object?)candidate.SourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", candidate.Analysis.Kind.ToString());
        command.Parameters.AddWithValue("$confidence", candidate.Analysis.Confidence);
        command.Parameters.AddWithValue("$title", EvidencePolicy.Truncate(candidate.Analysis.SuggestedTitle) ?? string.Empty);
        command.Parameters.AddWithValue("$reason", EvidencePolicy.Truncate(candidate.Analysis.Reason) ?? "Review candidate");
        command.Parameters.AddWithValue("$evidence", (object?)EvidencePolicy.Truncate(candidate.Analysis.EvidenceSnippet) ?? DBNull.Value);
        command.Parameters.AddWithValue("$dueAt", (object?)candidate.Analysis.DueAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", candidate.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$snoozeUntil", (object?)candidate.SnoozeUntil?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sender", (object?)EvidencePolicy.Truncate(candidate.SourceSenderDisplay) ?? DBNull.Value);
        command.Parameters.AddWithValue("$receivedAt", (object?)candidate.SourceReceivedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$recipientRole", candidate.SourceRecipientRole.ToString());
        command.Parameters.AddWithValue("$suppressed", candidate.Suppressed ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasOpenLlmFailureReviewCandidateForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM review_candidates
            WHERE source_id_hash = $source
              AND suppressed = 0
              AND resolved_at IS NULL
              AND reason LIKE 'LLM 분석 실패(%'
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$source", sourceIdHash);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<int> SuppressOpenLlmFailureReviewCandidatesForSourceAsync(string sourceIdHash, DateTimeOffset now, string resolution, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE review_candidates
            SET suggested_title = $title,
                reason = $reason,
                evidence_snippet = NULL,
                source_id = NULL,
                source_sender_display = NULL,
                source_received_at = NULL,
                source_recipient_role = $recipientRole,
                suppressed = 1,
                resolved_at = $resolvedAt,
                resolution = $resolution
            WHERE source_id_hash = $source
              AND suppressed = 0
              AND resolved_at IS NULL
              AND reason LIKE 'LLM 분석 실패(%'
            """;
        command.Parameters.AddWithValue("$source", sourceIdHash);
        command.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        command.Parameters.AddWithValue("$reason", "LLM 재분석으로 검토 후보를 정리했습니다.");
        command.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
        command.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$resolution", string.IsNullOrWhiteSpace(resolution) ? "Suppressed" : resolution);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        command.CommandText = "SELECT id, title, due_at, source_id_hash, source_id, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted, source_sender_display, source_received_at, source_recipient_role, kind FROM tasks WHERE status IN ('Open','Snoozed') ORDER BY due_at IS NULL, due_at, created_at";
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
            SELECT id, source_id_hash, source_id, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, suppressed, snooze_until, source_sender_display, source_received_at, source_recipient_role
            FROM review_candidates
            WHERE suppressed = 0
              AND resolved_at IS NULL
              AND (snooze_until IS NULL OR snooze_until <= $now)
            ORDER BY created_at DESC
            LIMIT 100
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
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
            SET source_id = NULL,
                suppressed = 1,
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
            candidate.SourceId,
            Math.Clamp(candidate.Analysis.Confidence, 0, 1),
            EvidencePolicy.Truncate(candidate.Analysis.Reason) ?? "검토 후보에서 등록",
            EvidencePolicy.Truncate(candidate.Analysis.EvidenceSnippet),
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: candidate.SourceSenderDisplay,
            SourceReceivedAt: candidate.SourceReceivedAt,
            SourceRecipientRole: candidate.SourceRecipientRole,
            Kind: candidate.Analysis.Kind);

        await SaveTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return task;
    }

    public async Task<bool> SnoozeReviewCandidateAsync(Guid candidateId, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE review_candidates
            SET snooze_until = $snoozeUntil
            WHERE id = $id AND suppressed = 0 AND resolved_at IS NULL
            """;
        update.Parameters.AddWithValue("$id", candidateId.ToString());
        var effectiveUntil = until <= now ? now.AddHours(1) : until;
        update.Parameters.AddWithValue("$snoozeUntil", effectiveUntil.ToUniversalTime().ToString("O"));
        var rows = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
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
                source_id = NULL,
                source_sender_display = NULL,
                source_received_at = NULL,
                source_recipient_role = $recipientRole,
                suppressed = 1,
                resolved_at = $resolvedAt,
                resolution = $resolution
            WHERE id = $id AND suppressed = 0 AND resolved_at IS NULL
            """;
        update.Parameters.AddWithValue("$id", candidateId.ToString());
        update.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        update.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        update.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
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

    public async Task<bool> DismissTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET status = $status,
                updated_at = $updatedAt
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$status", LocalTaskStatus.Dismissed.ToString());
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> CompleteTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET status = $status,
                snooze_until = NULL,
                updated_at = $updatedAt
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$status", LocalTaskStatus.Done.ToString());
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> SnoozeTaskAsync(Guid taskId, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET status = $status,
                snooze_until = $snoozeUntil,
                updated_at = $updatedAt
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        var effectiveUntil = until <= now ? now.AddHours(1) : until;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$status", LocalTaskStatus.Snoozed.ToString());
        command.Parameters.AddWithValue("$snoozeUntil", effectiveUntil.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> UpdateTaskDueAtAsync(Guid taskId, DateTimeOffset dueAt, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET due_at = $dueAt,
                updated_at = $updatedAt,
                status = CASE WHEN status = 'Snoozed' THEN 'Open' ELSE status END,
                snooze_until = NULL
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$dueAt", dueAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
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
        command.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_id = NULL, source_sender_display = NULL, source_received_at = NULL, source_recipient_role = $recipientRole, source_derived_data_deleted = 1, updated_at = $updated WHERE id = $id";
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        command.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        command.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
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
        taskCommand.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_id = NULL, source_sender_display = NULL, source_received_at = NULL, source_recipient_role = $recipientRole, source_derived_data_deleted = 1, updated_at = $updated WHERE source_id_hash = $source";
        taskCommand.Parameters.AddWithValue("$source", sourceIdHash);
        taskCommand.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        taskCommand.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        taskCommand.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
        taskCommand.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await taskCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var candidateCommand = connection.CreateCommand();
        candidateCommand.Transaction = transaction;
        candidateCommand.CommandText = "UPDATE review_candidates SET suggested_title = $title, reason = $reason, evidence_snippet = NULL, source_id = NULL, source_sender_display = NULL, source_received_at = NULL, source_recipient_role = $recipientRole WHERE source_id_hash = $source";
        candidateCommand.Parameters.AddWithValue("$source", sourceIdHash);
        candidateCommand.Parameters.AddWithValue("$title", LocalTaskItem.RedactedTitle);
        candidateCommand.Parameters.AddWithValue("$reason", LocalTaskItem.RedactedReason);
        candidateCommand.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
        await candidateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveTaskAsync(SqliteConnection connection, SqliteTransaction? transaction, LocalTaskItem task, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO tasks
            (id, title, due_at, source_id_hash, source_id, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted, source_sender_display, source_received_at, source_recipient_role, kind)
            VALUES ($id, $title, $dueAt, $source, $sourceId, $confidence, $reason, $evidence, $status, $snooze, $created, $updated, $deleted, $sender, $receivedAt, $recipientRole, $kind)
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", EvidencePolicy.Truncate(task.Title) ?? LocalTaskItem.RedactedTitle);
        command.Parameters.AddWithValue("$dueAt", (object?)task.DueAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)task.SourceIdHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceId", (object?)task.SourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", task.Confidence);
        command.Parameters.AddWithValue("$reason", EvidencePolicy.Truncate(task.Reason) ?? LocalTaskItem.RedactedReason);
        command.Parameters.AddWithValue("$evidence", (object?)EvidencePolicy.Truncate(task.EvidenceSnippet) ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$snooze", (object?)task.SnoozeUntil?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deleted", task.SourceDerivedDataDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$sender", (object?)EvidencePolicy.Truncate(task.SourceSenderDisplay) ?? DBNull.Value);
        command.Parameters.AddWithValue("$receivedAt", (object?)task.SourceReceivedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$recipientRole", task.SourceRecipientRole.ToString());
        command.Parameters.AddWithValue("$kind", task.Kind.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReviewCandidate?> ReadActiveReviewCandidateAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid candidateId, CancellationToken cancellationToken)
    {
        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT id, source_id_hash, source_id, kind, confidence, suggested_title, reason, evidence_snippet, due_at, created_at, suppressed, snooze_until, source_sender_display, source_received_at, source_recipient_role
            FROM review_candidates
            WHERE id = $id
              AND suppressed = 0
              AND resolved_at IS NULL
              AND (snooze_until IS NULL OR snooze_until <= $now)
            LIMIT 1
            """;
        lookup.Parameters.AddWithValue("$id", candidateId.ToString());
        lookup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

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
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetDouble(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            Enum.Parse<LocalTaskStatus>(reader.GetString(8)),
            MaybeDate(reader.GetValue(9)),
            DateTimeOffset.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.GetInt32(12) == 1,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            MaybeDate(reader.GetValue(14)),
            Enum.TryParse<MailboxRecipientRole>(reader.IsDBNull(15) ? null : reader.GetString(15), out var role) ? role : MailboxRecipientRole.Direct,
            Enum.TryParse<FollowUpKind>(reader.IsDBNull(16) ? null : reader.GetString(16), out var kind) ? kind : FollowUpKind.ActionRequested);
    }

    private static ReviewCandidate ReadCandidate(SqliteDataReader reader)
    {
        static DateTimeOffset? MaybeDate(object value) => value == DBNull.Value ? null : DateTimeOffset.Parse((string)value);

        var analysis = new FollowUpAnalysis(
            Enum.Parse<FollowUpKind>(reader.GetString(3)),
            AnalysisDisposition.Review,
            reader.GetDouble(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            MaybeDate(reader.GetValue(8)));

        return new ReviewCandidate(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            analysis,
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.GetInt32(10) == 1,
            MaybeDate(reader.GetValue(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            MaybeDate(reader.GetValue(13)),
            Enum.TryParse<MailboxRecipientRole>(reader.IsDBNull(14) ? null : reader.GetString(14), out var role) ? role : MailboxRecipientRole.Direct);
    }
}
