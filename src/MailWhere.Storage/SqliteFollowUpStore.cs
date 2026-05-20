using System.Text.Json;
using Microsoft.Data.Sqlite;
using MailWhere.Core.Domain;
using MailWhere.Core.Storage;

namespace MailWhere.Storage;

public sealed class SqliteFollowUpStore : IFollowUpStore, IAppStateStore
{
    private const string TaskColumns = "id, title, due_at, source_id_hash, source_id, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted, source_sender_display, source_received_at, source_recipient_role, kind, source_conversation_id, source_recipient_display_names";
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
        await EnsureColumnAsync(connection, "tasks", "source_conversation_id", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "tasks", "source_recipient_display_names", "TEXT NULL", cancellationToken).ConfigureAwait(false);

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
        await SaveReviewCandidateAsync(connection, null, candidate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TrySaveTaskWithProcessedSourcesAsync(LocalTaskItem task, string? actionSignature, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        if (!await TryMarkSourceProcessedAsync(connection, transaction, task.SourceIdHash, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await TryMarkActionSignatureOrCommitDuplicateAsync(connection, transaction, actionSignature, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await SaveTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TrySaveReviewCandidateWithProcessedSourcesAsync(ReviewCandidate candidate, string? actionSignature, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        if (!await TryMarkSourceProcessedAsync(connection, transaction, candidate.SourceIdHash, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await TryMarkActionSignatureOrCommitDuplicateAsync(connection, transaction, actionSignature, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await SaveReviewCandidateAsync(connection, transaction, candidate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TryMarkProcessedSourcesAsync(string sourceIdHash, string? actionSignature, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        if (!await TryMarkSourceProcessedAsync(connection, transaction, sourceIdHash, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await TryMarkActionSignatureOrCommitDuplicateAsync(connection, transaction, actionSignature, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
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
        command.Parameters.AddWithValue("$reason", "LLM 재분석으로 확인 필요 항목을 정리했습니다.");
        command.Parameters.AddWithValue("$recipientRole", MailboxRecipientRole.Other.ToString());
        command.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$resolution", string.IsNullOrWhiteSpace(resolution) ? "Suppressed" : resolution);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await MarkSourceProcessedAsync(connection, null, sourceIdHash, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordReplyObservationAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email.ConversationId) || string.IsNullOrWhiteSpace(email.SenderDisplay))
        {
            return;
        }

        var participantKey = ReplyProgressMatcher.NormalizeParticipantKey(email.SenderDisplay);
        if (participantKey.Length == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO reply_receipts
            (conversation_id, participant_key, participant_display, received_at, source_id_hash)
            VALUES ($conversation, $participantKey, $participantDisplay, $receivedAt, $sourceHash)
            """;
        command.Parameters.AddWithValue("$conversation", email.ConversationId.Trim());
        command.Parameters.AddWithValue("$participantKey", participantKey);
        command.Parameters.AddWithValue("$participantDisplay", EvidencePolicy.Truncate(email.SenderDisplay) ?? email.SenderDisplay.Trim());
        command.Parameters.AddWithValue("$receivedAt", email.ReceivedAt.ToString("O"));
        command.Parameters.AddWithValue("$sourceHash", email.SourceHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT {TaskColumns} FROM tasks WHERE status IN ('Open','Snoozed') ORDER BY due_at IS NULL, due_at, created_at";
        var now = DateTimeOffset.UtcNow;
        var tasks = new List<LocalTaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var task = ReadTask(reader);
            if (FollowUpPresentation.IsVisibleInPrimary(task, now))
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    public async Task<IReadOnlyList<LocalTaskItem>> ListArchivedTasksAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT {TaskColumns} FROM tasks WHERE status = 'Archived' ORDER BY updated_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var tasks = new List<LocalTaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task<IReadOnlyList<ReplyProgressItem>> ListReplyProgressAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tasks = new List<LocalTaskItem>();
        var taskCommand = connection.CreateCommand();
        taskCommand.CommandText = $"SELECT {TaskColumns} FROM tasks WHERE status IN ('Open','Snoozed') AND kind = 'WaitingForReply' AND source_conversation_id IS NOT NULL ORDER BY due_at IS NULL, due_at, created_at";
        await using (var reader = await taskCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }
        }

        if (tasks.Count == 0)
        {
            return Array.Empty<ReplyProgressItem>();
        }

        var receipts = new List<ReplyReceipt>();
        var receiptCommand = connection.CreateCommand();
        receiptCommand.CommandText = "SELECT conversation_id, participant_display, received_at, source_id_hash FROM reply_receipts";
        await using (var reader = await receiptCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                receipts.Add(new ReplyReceipt(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return tasks
            .Select(task => ReplyProgressMatcher.Build(task, receipts))
            .Where(progress => progress is not null)
            .Cast<ReplyProgressItem>()
            .ToArray();
    }

    public async Task<IReadOnlyList<WaitingClosureSuggestion>> ListWaitingClosureSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT suggestion.id, suggestion.task_id, suggestion.task_title, suggestion.trigger_source_hash,
                   suggestion.trigger_kind, suggestion.decision_source, suggestion.confidence, suggestion.reason,
                   suggestion.triggered_at, suggestion.created_at
            FROM waiting_closure_suggestions suggestion
            JOIN tasks task ON task.id = suggestion.task_id
            WHERE suggestion.resolved_at IS NULL
              AND task.status IN ('Open','Snoozed')
            ORDER BY suggestion.created_at DESC
            LIMIT 50
            """;
        var suggestions = new List<WaitingClosureSuggestion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            suggestions.Add(ReadWaitingClosureSuggestion(reader));
        }

        return suggestions;
    }

    public async Task<bool> SaveWaitingClosureSuggestionAsync(WaitingClosureSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO waiting_closure_suggestions
            (id, task_id, task_title, trigger_source_hash, trigger_kind, decision_source, confidence, reason, triggered_at, created_at)
            VALUES ($id, $taskId, $taskTitle, $triggerSource, $triggerKind, $decisionSource, $confidence, $reason, $triggeredAt, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", suggestion.Id.ToString());
        command.Parameters.AddWithValue("$taskId", suggestion.TaskId.ToString());
        command.Parameters.AddWithValue("$taskTitle", EvidencePolicy.Truncate(suggestion.TaskTitle) ?? string.Empty);
        command.Parameters.AddWithValue("$triggerSource", suggestion.TriggerSourceHash);
        command.Parameters.AddWithValue("$triggerKind", suggestion.TriggerKind.ToString());
        command.Parameters.AddWithValue("$decisionSource", suggestion.DecisionSource.ToString());
        command.Parameters.AddWithValue("$confidence", suggestion.Confidence);
        command.Parameters.AddWithValue("$reason", EvidencePolicy.Truncate(suggestion.Reason) ?? "회신 감지");
        command.Parameters.AddWithValue("$triggeredAt", suggestion.TriggeredAt.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", suggestion.CreatedAt.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> ResolveWaitingClosureSuggestionAsync(Guid suggestionId, WaitingClosureResolution resolution, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT task_id FROM waiting_closure_suggestions WHERE id = $id AND resolved_at IS NULL LIMIT 1";
        lookup.Parameters.AddWithValue("$id", suggestionId.ToString());
        var taskIdValue = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (taskIdValue is null || taskIdValue == DBNull.Value)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (resolution == WaitingClosureResolution.Archived)
        {
            var archive = connection.CreateCommand();
            archive.Transaction = transaction;
            archive.CommandText = """
                UPDATE tasks
                SET status = $status,
                    snooze_until = NULL,
                    updated_at = $updatedAt
                WHERE id = $taskId
                  AND status IN ('Open','Snoozed')
                """;
            archive.Parameters.AddWithValue("$taskId", Convert.ToString(taskIdValue));
            archive.Parameters.AddWithValue("$status", LocalTaskStatus.Archived.ToString());
            archive.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            if (await archive.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        var resolve = connection.CreateCommand();
        resolve.Transaction = transaction;
        resolve.CommandText = """
            UPDATE waiting_closure_suggestions
            SET resolved_at = $resolvedAt,
                resolution = $resolution
            WHERE id = $id
              AND resolved_at IS NULL
            """;
        resolve.Parameters.AddWithValue("$id", suggestionId.ToString());
        resolve.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
        resolve.Parameters.AddWithValue("$resolution", resolution.ToString());
        var resolved = await resolve.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (!resolved)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
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
            EvidencePolicy.Truncate(candidate.Analysis.Reason) ?? "확인 필요에서 등록",
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
        await MarkSourceProcessedAsync(connection, transaction, candidate.SourceIdHash, cancellationToken).ConfigureAwait(false);

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

        var candidate = await ReadActiveReviewCandidateAsync(connection, transaction, candidateId, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

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

        await MarkSourceProcessedAsync(connection, transaction, candidate.SourceIdHash, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ArchiveTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await FinalizeTaskAsync(taskId, LocalTaskStatus.Archived, now, clearSnooze: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RestoreArchivedTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
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
              AND status = 'Archived'
            """;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$status", LocalTaskStatus.Open.ToString());
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> DismissTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await FinalizeTaskAsync(taskId, LocalTaskStatus.Dismissed, now, clearSnooze: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CompleteTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await FinalizeTaskAsync(taskId, LocalTaskStatus.Done, now, clearSnooze: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FinalizeTaskAsync(Guid taskId, LocalTaskStatus status, DateTimeOffset now, bool clearSnooze, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT source_id_hash FROM tasks WHERE id = $id AND status IN ('Open','Snoozed') LIMIT 1";
        lookup.Parameters.AddWithValue("$id", taskId.ToString());
        string? sourceHash;
        await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            sourceHash = reader.IsDBNull(0) ? null : reader.GetString(0);
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks
            SET status = $status,
                snooze_until = CASE WHEN $clearSnooze = 1 THEN NULL ELSE snooze_until END,
                updated_at = $updatedAt
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        command.Parameters.AddWithValue("$id", taskId.ToString());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$clearSnooze", clearSnooze ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        var updated = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (!updated)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await MarkSourceProcessedAsync(connection, transaction, sourceHash, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
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

    public async Task<LocalTaskItem?> UpdateTaskDetailsAsync(Guid taskId, TaskEditRequest edit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var normalized = TaskEditRequest.Create(edit.Title, edit.Kind, edit.DueAt);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE tasks
            SET title = $title,
                due_at = $dueAt,
                kind = $kind,
                updated_at = $updatedAt,
                status = CASE WHEN status = 'Snoozed' THEN 'Open' ELSE status END,
                snooze_until = NULL
            WHERE id = $id
              AND status IN ('Open','Snoozed')
            """;
        update.Parameters.AddWithValue("$id", taskId.ToString());
        update.Parameters.AddWithValue("$title", normalized.Title);
        update.Parameters.AddWithValue("$dueAt", normalized.DueAt is null ? DBNull.Value : normalized.DueAt.Value.ToString("O"));
        update.Parameters.AddWithValue("$kind", normalized.Kind.ToString());
        update.Parameters.AddWithValue("$updatedAt", now.ToString("O"));

        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT {TaskColumns} FROM tasks WHERE id = $id LIMIT 1";
        select.Parameters.AddWithValue("$id", taskId.ToString());

        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var updated = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadTask(reader)
            : null;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
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
        command.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_id = NULL, source_sender_display = NULL, source_received_at = NULL, source_recipient_role = $recipientRole, source_conversation_id = NULL, source_recipient_display_names = NULL, source_derived_data_deleted = 1, updated_at = $updated WHERE id = $id";
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
        taskCommand.CommandText = "UPDATE tasks SET title = $title, reason = $reason, evidence_snippet = NULL, source_id = NULL, source_sender_display = NULL, source_received_at = NULL, source_recipient_role = $recipientRole, source_conversation_id = NULL, source_recipient_display_names = NULL, source_derived_data_deleted = 1, updated_at = $updated WHERE source_id_hash = $source";
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
            (id, title, due_at, source_id_hash, source_id, confidence, reason, evidence_snippet, status, snooze_until, created_at, updated_at, source_derived_data_deleted, source_sender_display, source_received_at, source_recipient_role, kind, source_conversation_id, source_recipient_display_names)
            VALUES ($id, $title, $dueAt, $source, $sourceId, $confidence, $reason, $evidence, $status, $snooze, $created, $updated, $deleted, $sender, $receivedAt, $recipientRole, $kind, $conversation, $recipients)
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
        command.Parameters.AddWithValue("$conversation", (object?)EvidencePolicy.Truncate(task.SourceConversationId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$recipients", (object?)SerializeRecipients(task.SourceRecipientDisplayNames) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveReviewCandidateAsync(SqliteConnection connection, SqliteTransaction? transaction, ReviewCandidate candidate, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
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

    private static async Task MarkSourceProcessedAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string? sourceIdHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceIdHash))
        {
            return;
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO processed_sources (source_id_hash, processed_at) VALUES ($source, $processedAt)";
        command.Parameters.AddWithValue("$source", sourceIdHash);
        command.Parameters.AddWithValue("$processedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryMarkSourceProcessedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? sourceIdHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceIdHash))
        {
            return false;
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO processed_sources (source_id_hash, processed_at) VALUES ($source, $processedAt)";
        command.Parameters.AddWithValue("$source", sourceIdHash);
        command.Parameters.AddWithValue("$processedAt", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static async Task<bool> TryMarkActionSignatureOrCommitDuplicateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? actionSignature,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionSignature))
        {
            return true;
        }

        if (await TryMarkSourceProcessedAsync(connection, transaction, actionSignature, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return false;
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

    private static string? SerializeRecipients(IReadOnlyList<string>? recipients)
    {
        if (recipients is null || recipients.Count == 0)
        {
            return null;
        }

        var compact = recipients
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
            .Select(recipient => EvidencePolicy.Truncate(recipient.Trim()) ?? recipient.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return compact.Length == 0 ? null : JsonSerializer.Serialize(compact);
    }

    private static IReadOnlyList<string>? DeserializeRecipients(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var recipients = JsonSerializer.Deserialize<string[]>(raw);
            var compact = recipients?
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Select(recipient => recipient.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return compact is { Length: > 0 } ? compact : null;
        }
        catch (JsonException)
        {
            return null;
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
            Enum.TryParse<FollowUpKind>(reader.IsDBNull(16) ? null : reader.GetString(16), out var kind) ? kind : FollowUpKind.ActionRequested,
            SourceConversationId: reader.IsDBNull(17) ? null : reader.GetString(17),
            SourceRecipientDisplayNames: reader.IsDBNull(18) ? null : DeserializeRecipients(reader.GetString(18)));
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

    private static WaitingClosureSuggestion ReadWaitingClosureSuggestion(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        Enum.TryParse<WaitingClosureTriggerKind>(reader.GetString(4), out var triggerKind) ? triggerKind : WaitingClosureTriggerKind.RecipientReply,
        Enum.TryParse<WaitingClosureDecisionSource>(reader.GetString(5), out var source) ? source : WaitingClosureDecisionSource.Rule,
        reader.GetDouble(6),
        reader.GetString(7),
        DateTimeOffset.Parse(reader.GetString(8)),
        DateTimeOffset.Parse(reader.GetString(9)));
}
