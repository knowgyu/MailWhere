using System.Text;
using System.Text.Json;
using MailWhere.Core.Domain;
using MailWhere.Core.Search;
using Microsoft.Data.Sqlite;

namespace MailWhere.Storage;

public sealed class SqliteMailMirrorStore : IMailMirrorStore
{
    public const int MaxWriteBatchSize = 25;
    private const int MaxSearchLimit = 100;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private SqliteConnection? _readerConnection;
    private string _tokenizer = "unicode61";

    public SqliteMailMirrorStore(string databasePath)
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
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
            _tokenizer = await ChooseTokenizerAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, MailMirrorSchema.TablesSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, MailMirrorSchema.FtsSql(_tokenizer), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, MailMirrorSchema.TriggersSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, MailMirrorSchema.IndexesSql, cancellationToken).ConfigureAwait(false);
            await SetMetaAsync(connection, "tokenizer", _tokenizer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writerGate.Release();
        }

        await InvalidateReaderAsync().ConfigureAwait(false);
        await PrewarmAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await GetReaderConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "SELECT 1;", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _readerGate.Release();
        }
    }

    public async Task<MailMirrorBatchResult> UpsertBatchAsync(
        IReadOnlyList<MailMirrorMessage> messages,
        MailMirrorCheckpoint? checkpoint = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count > MaxWriteBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(messages), $"Mail mirror write batches must be {MaxWriteBatchSize} rows or fewer.");
        }

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);

            var upserted = 0;
            foreach (var batch in messages.Chunk(MaxWriteBatchSize))
            {
                using var transaction = connection.BeginTransaction();
                foreach (var message in batch)
                {
                    upserted += await UpsertAsync(connection, transaction, message, cancellationToken).ConfigureAwait(false);
                }

                if (checkpoint is not null)
                {
                    await SaveCheckpointAsync(connection, transaction, checkpoint, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (messages.Count == 0 && checkpoint is not null)
            {
                using var transaction = connection.BeginTransaction();
                await SaveCheckpointAsync(connection, transaction, checkpoint, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new MailMirrorBatchResult(upserted, checkpoint);
        }
        finally
        {
            _writerGate.Release();
            await InvalidateReaderAsync().ConfigureAwait(false);
        }
    }

    public async Task<int> DeleteAsync(IReadOnlyList<MailMirrorLocator> locators, CancellationToken cancellationToken = default)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            var deleted = 0;
            foreach (var locator in locators.Where(locator => locator.IsValid))
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM mail_messages WHERE store_id = $store AND entry_id = $entry";
                command.Parameters.AddWithValue("$store", locator.StoreId.Trim());
                command.Parameters.AddWithValue("$entry", locator.EntryId.Trim());
                deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        finally
        {
            _writerGate.Release();
            await InvalidateReaderAsync().ConfigureAwait(false);
        }
    }

    public async Task<int> ReconcileFolderAsync(string folder, IReadOnlyList<MailMirrorLocator> currentLocators, CancellationToken cancellationToken = default)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "CREATE TEMP TABLE IF NOT EXISTS mail_mirror_seen(store_id TEXT NOT NULL, entry_id TEXT NOT NULL, PRIMARY KEY(store_id, entry_id));", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "DELETE FROM mail_mirror_seen;", cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            foreach (var locator in currentLocators.Where(locator => locator.IsValid).Distinct())
            {
                var seen = connection.CreateCommand();
                seen.Transaction = transaction;
                seen.CommandText = "INSERT OR IGNORE INTO mail_mirror_seen(store_id, entry_id) VALUES ($store, $entry)";
                seen.Parameters.AddWithValue("$store", locator.StoreId.Trim());
                seen.Parameters.AddWithValue("$entry", locator.EntryId.Trim());
                await seen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM mail_messages
                WHERE folder = $folder
                  AND NOT EXISTS (
                      SELECT 1 FROM mail_mirror_seen seen
                      WHERE seen.store_id = mail_messages.store_id
                        AND seen.entry_id = mail_messages.entry_id
                  )
                """;
            delete.Parameters.AddWithValue("$folder", folder.Trim());
            var deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var generation = connection.CreateCommand();
            generation.Transaction = transaction;
            generation.CommandText = """
                INSERT INTO mail_mirror_generations (id, folder, completed_at, seen_count, deleted_count)
                VALUES ($id, $folder, $completed, $seen, $deleted)
                """;
            generation.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            generation.Parameters.AddWithValue("$folder", folder.Trim());
            generation.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.ToString("O"));
            generation.Parameters.AddWithValue("$seen", currentLocators.Count(locator => locator.IsValid));
            generation.Parameters.AddWithValue("$deleted", deleted);
            await generation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        finally
        {
            _writerGate.Release();
            await InvalidateReaderAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<MailMirrorSearchResult>> SearchAsync(MailMirrorSearchRequest request, CancellationToken cancellationToken = default)
    {
        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await GetReaderConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var query = MailMirrorText.Normalize(request.Query);
                return await SearchCoreAsync(connection, request, useFts: !string.IsNullOrWhiteSpace(query) && query.Length >= 3, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException) when (!string.IsNullOrWhiteSpace(request.Query))
            {
                return await SearchCoreAsync(connection, request, useFts: false, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _readerGate.Release();
        }
    }

    public async Task<string?> GetCheckpointAsync(string folder, CancellationToken cancellationToken = default)
    {
        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await GetReaderConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = "SELECT checkpoint FROM mail_mirror_checkpoints WHERE folder = $folder";
            command.Parameters.AddWithValue("$folder", folder.Trim());
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? null : Convert.ToString(value);
        }
        finally
        {
            _readerGate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<MailMirrorLocator, DateTimeOffset>> GetKnownLastModifiedAsync(
        IReadOnlyList<MailMirrorLocator> locators,
        CancellationToken cancellationToken = default)
    {
        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await GetReaderConnectionAsync(cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<MailMirrorLocator, DateTimeOffset>();
            foreach (var locator in locators.Where(locator => locator.IsValid).Distinct())
            {
                var command = connection.CreateCommand();
                command.CommandText = "SELECT last_modified_at FROM mail_messages WHERE store_id = $store AND entry_id = $entry";
                command.Parameters.AddWithValue("$store", locator.StoreId.Trim());
                command.Parameters.AddWithValue("$entry", locator.EntryId.Trim());
                var value = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (DateTimeOffset.TryParse(value, out var lastModified))
                {
                    result[locator] = lastModified;
                }
            }

            return result;
        }
        finally
        {
            _readerGate.Release();
        }
    }

    public async Task RebuildFtsAsync(CancellationToken cancellationToken = default)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "INSERT INTO mail_messages_fts(mail_messages_fts) VALUES('rebuild');", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writerGate.Release();
            await InvalidateReaderAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await InvalidateReaderAsync().ConfigureAwait(false);
        _writerGate.Dispose();
        _readerGate.Dispose();
    }

    private async Task<IReadOnlyList<MailMirrorSearchResult>> SearchCoreAsync(
        SqliteConnection connection,
        MailMirrorSearchRequest request,
        bool useFts,
        CancellationToken cancellationToken)
    {
        var where = new List<string>();
        var command = connection.CreateCommand();
        var query = MailMirrorText.Normalize(request.Query);
        var from = useFts
            ? "mail_messages m JOIN mail_messages_fts ON mail_messages_fts.rowid = m.id"
            : "mail_messages m";

        if (useFts)
        {
            where.Add("mail_messages_fts MATCH $query");
            command.Parameters.AddWithValue("$query", ToFtsQuery(query));
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add("(m.subject LIKE $like ESCAPE '\\' OR m.body_text LIKE $like ESCAPE '\\' OR m.sender_display LIKE $like ESCAPE '\\' OR m.recipients_text LIKE $like ESCAPE '\\')");
            command.Parameters.AddWithValue("$like", "%" + EscapeLike(query) + "%");
        }

        AddFilters(command, request, where);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, MaxSearchLimit);
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = $"""
            SELECT m.store_id, m.entry_id, m.folder, m.subject, m.sender_display,
                   m.received_at, m.sent_at, m.conversation_id, substr(m.body_text, 1, 160) AS snippet
            FROM {from}
            {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
            ORDER BY {(useFts ? "bm25(mail_messages_fts)," : string.Empty)} COALESCE(m.received_at, m.sent_at, '') DESC, m.store_id, m.entry_id
            LIMIT $limit
            """;

        var results = new List<MailMirrorSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MailMirrorSearchResult(
                new MailMirrorLocator(reader.GetString(0), reader.GetString(1)),
                Enum.TryParse<MailSourceFolder>(reader.GetString(2), out var folder) ? folder : MailSourceFolder.Inbox,
                reader.GetString(3),
                reader.GetString(4),
                ReadDate(reader, 5),
                ReadDate(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8)));
        }

        return results;
    }

    private static void AddFilters(SqliteCommand command, MailMirrorSearchRequest request, List<string> where)
    {
        if (request.Folder is not null)
        {
            where.Add("m.folder = $folder");
            command.Parameters.AddWithValue("$folder", request.Folder.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(request.SenderOrRecipient))
        {
            where.Add("(m.sender_display LIKE $party ESCAPE '\\' OR m.recipients_text LIKE $party ESCAPE '\\')");
            command.Parameters.AddWithValue("$party", "%" + EscapeLike(request.SenderOrRecipient.Trim()) + "%");
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationId))
        {
            where.Add("m.conversation_id = $conversation");
            command.Parameters.AddWithValue("$conversation", request.ConversationId.Trim());
        }

        if (request.From is not null)
        {
            where.Add("COALESCE(m.received_at, m.sent_at) >= $from");
            command.Parameters.AddWithValue("$from", request.From.Value.ToString("O"));
        }

        if (request.To is not null)
        {
            where.Add("COALESCE(m.received_at, m.sent_at) <= $to");
            command.Parameters.AddWithValue("$to", request.To.Value.ToString("O"));
        }
    }

    private static async Task<int> UpsertAsync(SqliteConnection connection, SqliteTransaction transaction, MailMirrorMessage message, CancellationToken cancellationToken)
    {
        if (!message.Locator.IsValid)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var normalizedBody = MailMirrorText.Normalize(message.BodyText);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mail_messages
            (store_id, entry_id, folder, received_at, sent_at, last_modified_at, conversation_id,
             subject, sender_display, recipients_text, body_text, body_hash, created_at, updated_at)
            VALUES
            ($store, $entry, $folder, $received, $sent, $modified, $conversation,
             $subject, $sender, $recipients, $body, $hash, $created, $updated)
            ON CONFLICT(store_id, entry_id) DO UPDATE SET
                folder = excluded.folder,
                received_at = excluded.received_at,
                sent_at = excluded.sent_at,
                last_modified_at = excluded.last_modified_at,
                conversation_id = excluded.conversation_id,
                subject = excluded.subject,
                sender_display = excluded.sender_display,
                recipients_text = excluded.recipients_text,
                body_text = excluded.body_text,
                body_hash = excluded.body_hash,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$store", message.StoreId.Trim());
        command.Parameters.AddWithValue("$entry", message.EntryId.Trim());
        command.Parameters.AddWithValue("$folder", message.Folder.ToString());
        command.Parameters.AddWithValue("$received", (object?)message.ReceivedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$sent", (object?)message.SentAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$modified", message.LastModifiedAt.ToString("O"));
        command.Parameters.AddWithValue("$conversation", (object?)Clean(message.ConversationId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$subject", MailMirrorText.Normalize(message.Subject));
        command.Parameters.AddWithValue("$sender", MailMirrorText.Normalize(message.SenderDisplay));
        command.Parameters.AddWithValue("$recipients", (object?)SerializeRecipients(message.RecipientDisplayNames) ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", normalizedBody);
        command.Parameters.AddWithValue("$hash", MailMirrorText.Hash(normalizedBody));
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$updated", now);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveCheckpointAsync(SqliteConnection connection, SqliteTransaction transaction, MailMirrorCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mail_mirror_checkpoints (folder, checkpoint, updated_at)
            VALUES ($folder, $checkpoint, $updated)
            ON CONFLICT(folder) DO UPDATE SET checkpoint = excluded.checkpoint, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$folder", checkpoint.Folder.Trim());
        command.Parameters.AddWithValue("$checkpoint", checkpoint.Value.Trim());
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetMetaAsync(SqliteConnection connection, string key, string value, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mail_mirror_meta (key, value, updated_at)
            VALUES ($key, $value, $updated)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ChooseTokenizerAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var name = "mailwhere_tokenizer_probe_" + Guid.NewGuid().ToString("N");
        try
        {
            await ExecuteAsync(connection, $"CREATE VIRTUAL TABLE temp.{name} USING fts5(value, tokenize='trigram');", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"DROP TABLE temp.{name};", cancellationToken).ConfigureAwait(false);
            return "trigram";
        }
        catch (SqliteException)
        {
            return "unicode61";
        }
    }

    private async Task<SqliteConnection> GetReaderConnectionAsync(CancellationToken cancellationToken)
    {
        if (_readerConnection is not null)
        {
            return _readerConnection;
        }

        var builder = new SqliteConnectionStringBuilder(_connectionString)
        {
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        _readerConnection = new SqliteConnection(builder.ToString());
        await _readerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(_readerConnection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
        return _readerConnection;
    }

    private async Task InvalidateReaderAsync()
    {
        await _readerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_readerConnection is not null)
            {
                await _readerConnection.DisposeAsync().ConfigureAwait(false);
                _readerConnection = null;
            }
        }
        finally
        {
            _readerGate.Release();
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ToFtsQuery(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => "\"" + term.Replace("\"", "\"\"") + "\"")
            .ToArray();
        return terms.Length == 0 ? "\"\"" : string.Join(" AND ", terms);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : MailMirrorText.Normalize(value);

    private static string? SerializeRecipients(IReadOnlyList<string>? recipients)
    {
        var clean = recipients?
            .Select(MailMirrorText.Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return clean is { Length: > 0 } ? JsonSerializer.Serialize(clean) : null;
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
}
