using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailWhere.Core.Domain;
using MailWhere.Core.Export;
using MailWhere.Core.Search;
using MailWhere.Storage;
using Microsoft.Data.Sqlite;

namespace MailWhere.Cli;

public static class CliApp
{
    public const string ProviderName = "MailWhere";
    public const string ContractVersion = "v1";

    public const int ExitSuccess = 0;
    public const int ExitExpectedUnavailable = 2;
    public const int ExitUsage = 64;
    public const int ExitUnexpected = 70;

    private const int DefaultArchivedLimit = 100;
    private const int DefaultListLimit = 50;
    private const int DefaultReviewLimit = 50;
    private const int DefaultMailSearchLimit = 20;
    private const int MaxLimit = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var generatedAt = DateTimeOffset.UtcNow;
        try
        {
            if (args.Length == 0)
            {
                await WriteErrorAsync(stdout, generatedAt, "usage", "Command is required. Run `manifest --json` for the command contract.").ConfigureAwait(false);
                return ExitUsage;
            }

            var command = args[0].Trim();
            var options = ParseOptions(args.Skip(1).ToArray());
            if (!options.Json)
            {
                await WriteErrorAsync(stdout, generatedAt, "usage", "The MailWhere CLI provider only emits JSON; pass --json.").ConfigureAwait(false);
                return ExitUsage;
            }

            return command switch
            {
                "health" => await HealthAsync(options, stdout, generatedAt).ConfigureAwait(false),
                "manifest" => await ManifestAsync(options, stdout, generatedAt).ConfigureAwait(false),
                "export" => await ExportAsync(options, stdout, generatedAt, cancellationToken).ConfigureAwait(false),
                "list-tasks" => await ListTasksAsync(options, stdout, generatedAt, cancellationToken).ConfigureAwait(false),
                "list-review-candidates" => await ListReviewCandidatesAsync(options, stdout, generatedAt, cancellationToken).ConfigureAwait(false),
                "search-mail" => await SearchMailAsync(options, stdout, generatedAt, cancellationToken).ConfigureAwait(false),
                _ => await UsageErrorAsync(stdout, generatedAt, $"Unknown command `{command}`.").ConfigureAwait(false)
            };
        }
        catch (UsageException ex)
        {
            await WriteErrorAsync(stdout, generatedAt, "usage", ex.Message).ConfigureAwait(false);
            return ExitUsage;
        }
        catch (DatabaseNotFoundException ex)
        {
            await WriteErrorAsync(stdout, generatedAt, "database-not-found", ex.Message).ConfigureAwait(false);
            return ExitExpectedUnavailable;
        }
        catch (SqliteException ex)
        {
            await WriteErrorAsync(stdout, generatedAt, "database-unavailable", $"Could not read the MailWhere database: {ex.SqliteErrorCode}.").ConfigureAwait(false);
            return ExitExpectedUnavailable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WriteErrorAsync(stdout, generatedAt, "database-unavailable", "Could not read the MailWhere database path.").ConfigureAwait(false);
            return ExitExpectedUnavailable;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            await WriteErrorAsync(stdout, generatedAt, "unexpected", "Unexpected MailWhere CLI provider failure.").ConfigureAwait(false);
            return ExitUnexpected;
        }
    }

    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localAppData, "MailWhere", "followups.sqlite");
    }

    private static Task<int> HealthAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt)
    {
        EnsureNoExtraOptions(options, allowed: []);
        var dbPath = GetDefaultDatabasePath();
        return WriteSuccessAsync(stdout, generatedAt, new
        {
            status = "ok",
            read_only = true,
            default_database_path = dbPath,
            database_exists = File.Exists(dbPath),
            commands = new[] { "health", "manifest", "export", "list-tasks", "list-review-candidates", "search-mail" }
        });
    }

    private static Task<int> ManifestAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt)
    {
        EnsureNoExtraOptions(options, allowed: []);
        return WriteSuccessAsync(stdout, generatedAt, new
        {
            provider = ProviderName,
            contract_version = ContractVersion,
            app_version = GetAppVersion(),
            read_only = true,
            no_outlook_com = true,
            privacy = new
            {
                excludes = new[]
                {
                    "raw_body",
                    "source_id",
                    "source_id_hash",
                    "evidence_snippet",
                    "full_recipient_lists",
                    "prompt_logs",
                    "api_keys",
                    "store_id",
                    "entry_id"
                }
            },
            exit_codes = new
            {
                success = ExitSuccess,
                expected_unavailable = ExitExpectedUnavailable,
                usage = ExitUsage,
                unexpected = ExitUnexpected
            },
            commands = new object[]
            {
                new
                {
                    name = "health",
                    usage = "health --json",
                    description = "Report CLI provider health without opening or creating the MailWhere database."
                },
                new
                {
                    name = "manifest",
                    usage = "manifest --json",
                    description = "Describe the v1 MailWhere CLI provider contract."
                },
                new
                {
                    name = "export",
                    usage = "export --json [--db PATH] [--archived-limit N]",
                    description = "Return the sanitized MailWhere export snapshot."
                },
                new
                {
                    name = "list-tasks",
                    usage = "list-tasks --json [--status open|archived|all] [--due-window today|overdue|7d|30d|none|all] [--limit N] [--db PATH]",
                    description = "Return sanitized task rows from the read-only database."
                },
                new
                {
                    name = "list-review-candidates",
                    usage = "list-review-candidates --json [--limit N] [--db PATH]",
                    description = "Return sanitized active review candidates from the read-only database."
                },
                new
                {
                    name = "search-mail",
                    usage = "search-mail --json --query TEXT [--folder inbox|sent|all] [--sender-recipient TEXT] [--conversation ID] [--limit N] [--db PATH]",
                    description = "Search the local SQLite mail mirror only; returns bounded snippets and opaque open_source_token values."
                }
            }
        });
    }

    private static async Task<int> ExportAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        EnsureNoExtraOptions(options, allowed: ["db", "archived-limit"]);
        var archivedLimit = options.GetPositiveInt("archived-limit", DefaultArchivedLimit, MaxLimit);
        var store = OpenReadOnlyStore(options.GetDatabasePath());
        var export = new MailWhereExportService(store);
        var snapshot = await export.BuildSnapshotAsync(generatedAt, archivedLimit, cancellationToken).ConfigureAwait(false);
        return await WriteSuccessAsync(stdout, generatedAt, snapshot).ConfigureAwait(false);
    }

    private static async Task<int> ListTasksAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        EnsureNoExtraOptions(options, allowed: ["db", "status", "due-window", "limit"]);
        var status = options.GetChoice("status", "open", ["open", "archived", "all"]);
        var dueWindow = options.GetChoice("due-window", "all", ["today", "overdue", "7d", "30d", "none", "all"]);
        var limit = options.GetPositiveInt("limit", DefaultListLimit, MaxLimit);
        var store = OpenReadOnlyStore(options.GetDatabasePath());

        var tasks = new List<MailWhereExportTask>();
        if (status is "open" or "all")
        {
            var openTasks = await store.ListOpenTasksAsync(cancellationToken).ConfigureAwait(false);
            tasks.AddRange(openTasks.Select(MailWhereExportTask.FromTask));
        }

        if (status is "archived" or "all")
        {
            var archivedTasks = await store.ListArchivedTasksAsync(Math.Max(limit, DefaultArchivedLimit), cancellationToken).ConfigureAwait(false);
            tasks.AddRange(archivedTasks.Select(MailWhereExportTask.FromTask));
        }

        var filtered = tasks
            .Where(task => MatchesDueWindow(task.DueAt, dueWindow, generatedAt))
            .Take(limit)
            .ToArray();

        return await WriteSuccessAsync(stdout, generatedAt, new
        {
            status,
            due_window = dueWindow,
            limit,
            tasks = filtered
        }).ConfigureAwait(false);
    }

    private static async Task<int> ListReviewCandidatesAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        EnsureNoExtraOptions(options, allowed: ["db", "limit"]);
        var limit = options.GetPositiveInt("limit", DefaultReviewLimit, MaxLimit);
        var store = OpenReadOnlyStore(options.GetDatabasePath());
        var candidates = await store.ListReviewCandidatesAsync(cancellationToken).ConfigureAwait(false);
        return await WriteSuccessAsync(stdout, generatedAt, new
        {
            limit,
            candidates = candidates
                .Take(limit)
                .Select(MailWhereExportReviewCandidate.FromCandidate)
                .ToArray()
        }).ConfigureAwait(false);
    }

    private static async Task<int> SearchMailAsync(CliOptions options, TextWriter stdout, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        EnsureNoExtraOptions(options, allowed: ["db", "query", "folder", "sender-recipient", "conversation", "limit"]);
        if (!options.Values.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            throw new UsageException("Option `--query` is required.");
        }

        var limit = options.GetPositiveInt("limit", DefaultMailSearchLimit, MaxLimit);
        var folder = options.GetChoice("folder", "all", ["inbox", "sent", "all"]);
        await using var mirror = OpenReadOnlyMirror(options.GetDatabasePath());
        var results = await mirror.SearchAsync(new MailMirrorSearchRequest(
            Query: query,
            SenderOrRecipient: options.Values.GetValueOrDefault("sender-recipient"),
            Folder: folder switch
            {
                "inbox" => MailSourceFolder.Inbox,
                "sent" => MailSourceFolder.Sent,
                _ => null
            },
            ConversationId: options.Values.GetValueOrDefault("conversation"),
            Limit: limit), cancellationToken).ConfigureAwait(false);

        return await WriteSuccessAsync(stdout, generatedAt, new
        {
            query = query.Trim(),
            folder,
            limit,
            results = results.Select(result => new
            {
                folder = result.Folder.ToString(),
                subject = result.Subject,
                sender_display = result.SenderDisplay,
                received_at = result.ReceivedAt,
                sent_at = result.SentAt,
                conversation_id = result.ConversationId,
                snippet = result.Snippet,
                open_source_token = result.OpenSourceToken
            }).ToArray()
        }).ConfigureAwait(false);
    }

    private static SqliteFollowUpStore OpenReadOnlyStore(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new DatabaseNotFoundException($"MailWhere database was not found at `{databasePath}`.");
        }

        return SqliteFollowUpStore.OpenReadOnly(databasePath);
    }

    private static SqliteMailMirrorStore OpenReadOnlyMirror(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new DatabaseNotFoundException($"MailWhere database was not found at `{databasePath}`.");
        }

        return new SqliteMailMirrorStore(databasePath);
    }

    private static bool MatchesDueWindow(DateTimeOffset? dueAt, string dueWindow, DateTimeOffset generatedAt)
    {
        if (dueWindow == "all")
        {
            return true;
        }

        if (dueWindow == "none")
        {
            return dueAt is null;
        }

        if (dueAt is null)
        {
            return false;
        }

        var localNow = generatedAt.ToLocalTime();
        var today = new DateTimeOffset(localNow.Date, localNow.Offset);
        var due = dueAt.Value.ToLocalTime();
        return dueWindow switch
        {
            "overdue" => due < today,
            "today" => due >= today && due < today.AddDays(1),
            "7d" => due >= today && due < today.AddDays(7),
            "30d" => due >= today && due < today.AddDays(30),
            _ => true
        };
    }

    private static CliOptions ParseOptions(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--json")
            {
                options.Json = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected positional argument `{arg}`.");
            }

            var name = arg[2..];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new UsageException("Empty option name.");
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Option `--{name}` requires a value.");
            }

            options.Values[name] = args[++i];
        }

        return options;
    }

    private static void EnsureNoExtraOptions(CliOptions options, IReadOnlyCollection<string> allowed)
    {
        foreach (var option in options.Values.Keys)
        {
            if (!allowed.Contains(option, StringComparer.Ordinal))
            {
                throw new UsageException($"Option `--{option}` is not supported for this command.");
            }
        }
    }

    private static Task<int> UsageErrorAsync(TextWriter stdout, DateTimeOffset generatedAt, string message) =>
        WriteErrorWithExitAsync(stdout, generatedAt, "usage", message, ExitUsage);

    private static async Task<int> WriteSuccessAsync(TextWriter stdout, DateTimeOffset generatedAt, object data)
    {
        await WriteEnvelopeAsync(stdout, new ProviderEnvelope(
            ProviderName,
            ContractVersion,
            GetAppVersion(),
            generatedAt,
            Ok: true,
            Data: data,
            Code: null,
            Message: null)).ConfigureAwait(false);
        return ExitSuccess;
    }

    private static Task WriteErrorAsync(TextWriter stdout, DateTimeOffset generatedAt, string code, string message) =>
        WriteEnvelopeAsync(stdout, new ProviderEnvelope(
            ProviderName,
            ContractVersion,
            GetAppVersion(),
            generatedAt,
            Ok: false,
            Data: null,
            Code: code,
            Message: message));

    private static async Task<int> WriteErrorWithExitAsync(TextWriter stdout, DateTimeOffset generatedAt, string code, string message, int exitCode)
    {
        await WriteErrorAsync(stdout, generatedAt, code, message).ConfigureAwait(false);
        return exitCode;
    }

    private static async Task WriteEnvelopeAsync(TextWriter stdout, ProviderEnvelope envelope)
    {
        await stdout.WriteLineAsync(JsonSerializer.Serialize(envelope, JsonOptions)).ConfigureAwait(false);
        await stdout.FlushAsync().ConfigureAwait(false);
    }

    private static string GetAppVersion()
    {
        var version = typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            ? typeof(CliApp).Assembly.GetName().Version?.ToString() ?? "unknown"
            : version;
    }

    private sealed class CliOptions
    {
        public bool Json { get; set; }
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string GetDatabasePath() => Values.TryGetValue("db", out var path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : GetDefaultDatabasePath();

        public int GetPositiveInt(string name, int defaultValue, int maxValue)
        {
            if (!Values.TryGetValue(name, out var raw))
            {
                return defaultValue;
            }

            if (!int.TryParse(raw, out var value) || value <= 0)
            {
                throw new UsageException($"Option `--{name}` must be a positive integer.");
            }

            return Math.Min(value, maxValue);
        }

        public string GetChoice(string name, string defaultValue, IReadOnlyCollection<string> allowed)
        {
            if (!Values.TryGetValue(name, out var value))
            {
                return defaultValue;
            }

            if (!allowed.Contains(value))
            {
                throw new UsageException($"Option `--{name}` must be one of: {string.Join(", ", allowed)}.");
            }

            return value;
        }
    }

    private sealed record ProviderEnvelope(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("contract_version")] string ContractVersion,
        [property: JsonPropertyName("app_version")] string AppVersion,
        [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("data")] object? Data,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);

    private sealed class UsageException(string message) : Exception(message);

    private sealed class DatabaseNotFoundException(string message) : Exception(message);
}
