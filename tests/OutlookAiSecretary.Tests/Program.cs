using Microsoft.Data.Sqlite;
using OutlookAiSecretary.Core.Analysis;
using OutlookAiSecretary.Core.Capabilities;
using OutlookAiSecretary.Core.Domain;
using OutlookAiSecretary.Core.LLM;
using OutlookAiSecretary.Core.Mail;
using OutlookAiSecretary.Core.Notifications;
using OutlookAiSecretary.Core.Pipeline;
using OutlookAiSecretary.Core.Reminders;
using OutlookAiSecretary.Core.Scanning;
using OutlookAiSecretary.Core.Storage;
using OutlookAiSecretary.Storage;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("Korean deadline request creates auto task", KoreanDeadlineRequestCreatesAutoTask),
    ("Meeting request is classified as meeting", MeetingRequestIsClassifiedAsMeeting),
    ("Korean weekday due date parses", KoreanWeekdayDueDateParses),
    ("FYI mail is ignored", FyiMailIsIgnored),
    ("Evidence is truncated", EvidenceIsTruncated),
    ("Managed mode blocks watcher without smoke gate", ManagedModeBlocksWatcherWithoutGate),
    ("Smoke gate is required even if managed mode is false", SmokeGateRequiredEvenIfManagedModeFalse),
    ("Ambiguous mail does not auto create", AmbiguousMailDoesNotAutoCreate),
    ("Pipeline suppresses duplicate source", PipelineSuppressesDuplicateSource),
    ("Manual task can be created", ManualTaskCanBeCreated),
    ("Not-a-task feedback persists", NotATaskFeedbackPersists),
    ("Notification throttle suppresses repeat alerts", NotificationThrottleSuppressesRepeatAlerts),
    ("Diagnostics exporter drops sensitive detail keys", DiagnosticsExporterDropsSensitiveDetailKeys),
    ("Diagnostics exporter sanitizes allowed detail values", DiagnosticsExporterSanitizesAllowedDetailValues),
    ("Runtime diagnostics export includes safe gate codes", RuntimeDiagnosticsExportIncludesSafeGateCodes),
    ("Partial runtime settings keep safe defaults", PartialRuntimeSettingsKeepSafeDefaults),
    ("Runtime settings map Ollama endpoint", RuntimeSettingsMapOllamaEndpoint),
    ("LLM JSON creates calendar task", LlmJsonCreatesCalendarTask),
    ("Invalid LLM JSON falls back to rules", InvalidLlmJsonFallsBackToRules),
    ("Recent mail scan honors request window", RecentMailScanHonorsRequestWindow),
    ("Reminder planner emits lookahead notifications", ReminderPlannerEmitsLookaheadNotifications),
    ("SQLite store truncates source-derived fields", SqliteStoreTruncatesSourceDerivedFields),
    ("SQLite delete source-derived data redacts task and candidate", SqliteDeleteSourceDerivedDataRedactsTaskAndCandidate),
    ("SQLite schema avoids raw mail columns", SqliteSchemaAvoidsRawMailColumns)
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

return failures == 0 ? 0 : 1;

static EmailSnapshot Mail(string subject, string body, string? id = null) => new(
    id ?? Guid.NewGuid().ToString("N"),
    new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9)),
    "tester",
    subject,
    body);

static async Task KoreanDeadlineRequestCreatesAutoTask()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail("A사업 자료 요청", "내일까지 비용 자료 검토 후 회신 부탁드립니다."));
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected auto task.");
    Assert(result.Confidence >= 0.8, "Expected high confidence.");
    Assert(result.DueAt is not null, "Expected due date.");
}

static async Task MeetingRequestIsClassifiedAsMeeting()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail("주간 sync", "내일 오전 회의 참석 부탁드립니다."));
    Assert(result.Kind == FollowUpKind.Meeting, "Expected meeting classification.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected meeting with due signal to auto-create.");
    Assert(result.DueAt is not null, "Expected relative due date.");
}

static Task KoreanWeekdayDueDateParses()
{
    var anchor = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9)); // Thursday
    var friday = SimpleDueDateParser.TryParse("이번 주 금요일까지 공유", anchor);
    var nextMonday = SimpleDueDateParser.TryParse("다음 주 월요일 회의", anchor);

    Assert(friday == new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)), "Expected this Friday.");
    Assert(nextMonday == new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.FromHours(9)), "Expected next Monday.");
    return Task.CompletedTask;
}

static async Task FyiMailIsIgnored()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail("공지", "FYI 참고용 뉴스레터입니다."));
    Assert(result.Disposition == AnalysisDisposition.Ignore, "Expected ignore.");
}

static Task EvidenceIsTruncated()
{
    var longText = new string('가', 500);
    var truncated = EvidencePolicy.Truncate(longText);
    Assert(truncated is not null && truncated.Length <= EvidencePolicy.MaxEvidenceChars + 1, "Expected capped evidence.");
    return Task.CompletedTask;
}

static Task ManagedModeBlocksWatcherWithoutGate()
{
    var result = FeatureGate.EvaluateAutomaticWatcher(new GateInput(
        ManagedMode: true,
        SmokeGatePassed: false,
        OutlookComAvailable: true,
        InboxReadable: true,
        BodyReadable: true,
        StorageWritable: true,
        LlmReachable: true,
        RuleOnlyModeAccepted: false));
    Assert(!result.AutomaticWatcherEnabled, "Watcher should be disabled without gate.");
    return Task.CompletedTask;
}

static Task SmokeGateRequiredEvenIfManagedModeFalse()
{
    var result = FeatureGate.EvaluateAutomaticWatcher(new GateInput(
        ManagedMode: false,
        SmokeGatePassed: false,
        OutlookComAvailable: true,
        InboxReadable: true,
        BodyReadable: true,
        StorageWritable: true,
        LlmReachable: false,
        RuleOnlyModeAccepted: true));
    Assert(!result.AutomaticWatcherEnabled, "Smoke gate should be unconditional for automatic watching.");
    Assert(result.Reasons.Any(reason => reason.Contains("smoke gate", StringComparison.OrdinalIgnoreCase)), "Expected smoke-gate reason.");
    return Task.CompletedTask;
}

static async Task AmbiguousMailDoesNotAutoCreate()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail("일정 관련", "금요일 이야기가 있었습니다."));
    Assert(result.Disposition != AnalysisDisposition.AutoCreateTask, "Ambiguous mail should not auto-create.");
}

static async Task PipelineSuppressesDuplicateSource()
{
    var store = new FakeStore();
    var pipeline = new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), store);
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "same-id");

    var first = await pipeline.ProcessAsync(mail);
    var second = await pipeline.ProcessAsync(mail);

    Assert(first.Kind == PipelineOutcomeKind.TaskCreated, "Expected first task.");
    Assert(second.Kind == PipelineOutcomeKind.Duplicate, "Expected duplicate suppression.");
    Assert(store.Tasks.Count == 1, "Expected one task.");
}

static async Task ManualTaskCanBeCreated()
{
    var store = new FakeStore();
    var service = new ManualTaskService(store);
    var task = await service.CreateAsync("CFO 메일 답장");
    Assert(task.SourceIdHash is null, "Manual task should not require source mail.");
    Assert(store.Tasks.Count == 1, "Expected persisted manual task.");
}

static async Task NotATaskFeedbackPersists()
{
    var store = new FakeStore();
    await store.MarkNotATaskAsync("abc");
    Assert(store.NotTaskSources.Contains("abc"), "Expected not-a-task source recorded.");
}

static Task NotificationThrottleSuppressesRepeatAlerts()
{
    var throttle = new NotificationThrottle(TimeSpan.FromHours(1));
    var now = DateTimeOffset.UtcNow;
    Assert(throttle.ShouldNotify("source", now), "First alert should pass.");
    Assert(!throttle.ShouldNotify("source", now.AddMinutes(5)), "Repeat alert should be suppressed.");
    Assert(throttle.ShouldNotify("source", now.AddHours(2)), "Later alert should pass.");
    return Task.CompletedTask;
}

static Task DiagnosticsExporterDropsSensitiveDetailKeys()
{
    var report = new CapabilityReport(DateTimeOffset.UtcNow, new[]
    {
        CapabilityProbeResult.Passed("probe", "subject secret should not be exported", new Dictionary<string, string>
        {
            ["count"] = "1",
            ["subject"] = "secret",
            ["senderAddress"] = "secret@example.com"
        })
    });
    var json = SanitizedDiagnosticsExporter.Export(report);
    Assert(json.Contains("count"), "Expected safe detail.");
    Assert(!json.Contains("secret", StringComparison.OrdinalIgnoreCase), "Expected sensitive details removed.");
    return Task.CompletedTask;
}

static Task DiagnosticsExporterSanitizesAllowedDetailValues()
{
    var report = new CapabilityReport(DateTimeOffset.UtcNow, new[]
    {
        CapabilityProbeResult.Passed("probe", "ok", new Dictionary<string, string>
        {
            ["feature"] = "secret subject text",
            ["mode"] = "manual",
            ["enabled"] = "yes",
            ["count"] = "12x",
            ["statusCode"] = "writable"
        })
    });

    var json = SanitizedDiagnosticsExporter.Export(report);
    Assert(!json.Contains("secret", StringComparison.OrdinalIgnoreCase), "Expected unsafe allowed-key value removed.");
    Assert(!json.Contains("12x", StringComparison.OrdinalIgnoreCase), "Expected non-numeric count removed.");
    Assert(!json.Contains("yes", StringComparison.OrdinalIgnoreCase), "Expected non-boolean enabled removed.");
    Assert(json.Contains("manual", StringComparison.OrdinalIgnoreCase), "Expected safe mode retained.");
    Assert(json.Contains("writable", StringComparison.OrdinalIgnoreCase), "Expected safe status code retained.");
    return Task.CompletedTask;
}

static Task RuntimeDiagnosticsExportIncludesSafeGateCodes()
{
    var report = new CapabilityReport(DateTimeOffset.UtcNow, new[]
    {
        CapabilityProbeResult.Passed("outlook-com", "ok"),
        CapabilityProbeResult.Passed("outlook-inbox", "ok"),
        CapabilityProbeResult.Passed("outlook-mail-body", "ok"),
        CapabilityProbeResult.Passed("storage-writable", "ok"),
        CapabilityProbeResult.Warning("llm-endpoint", "EndpointNotConfigured", new Dictionary<string, string> { ["feature"] = "llm-endpoint", ["enabled"] = "false" })
    });

    var snapshot = RuntimeGateComposer.Compose(RuntimeSettings.ManagedSafeDefault, report);
    var json = SanitizedDiagnosticsExporter.Export(snapshot);

    Assert(json.Contains("AutomaticWatcherGate"), "Expected gate result in diagnostics.");
    Assert(json.Contains("automatic-watcher-not-requested"), "Expected safe manual-mode reason code.");
    Assert(!json.Contains("EndpointNotConfigured", StringComparison.OrdinalIgnoreCase), "Expected raw probe messages omitted.");
    return Task.CompletedTask;
}

static Task PartialRuntimeSettingsKeepSafeDefaults()
{
    var partialJson = """
        {
          "AutomaticWatcherRequested": true,
          "RuleOnlyModeAccepted": true
        }
        """;
    var settings = RuntimeSettingsSerializer.ParseOrDefault(partialJson);

    Assert(settings.ManagedMode, "Missing ManagedMode should preserve managed-safe default.");
    Assert(!settings.SmokeGatePassed, "Missing SmokeGatePassed should preserve safe false default.");

    var report = new CapabilityReport(DateTimeOffset.UtcNow, new[]
    {
        CapabilityProbeResult.Passed("outlook-com", "ok"),
        CapabilityProbeResult.Passed("outlook-inbox", "ok"),
        CapabilityProbeResult.Passed("outlook-mail-body", "ok"),
        CapabilityProbeResult.Passed("storage-writable", "ok"),
        CapabilityProbeResult.Warning("llm-endpoint", "EndpointNotConfigured", new Dictionary<string, string> { ["feature"] = "llm-endpoint", ["enabled"] = "false" })
    });
    var snapshot = RuntimeGateComposer.Compose(settings, report);
    Assert(!snapshot.AutomaticWatcherGate.AutomaticWatcherEnabled, "Partial settings must not bypass smoke gate.");
    Assert(snapshot.AutomaticWatcherGate.Reasons.Any(reason => reason.Contains("smoke gate", StringComparison.OrdinalIgnoreCase)), "Expected smoke-gate gate reason.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsMapOllamaEndpoint()
{
    Environment.SetEnvironmentVariable("OAS_TEST_KEY", "test-token");
    var json = """
        {
          "ManagedMode": true,
          "ExternalLlmEnabled": true,
          "LlmProvider": "Ollama",
          "LlmEndpoint": "http://localhost:11434",
          "LlmModel": "qwen3.6",
          "LlmApiKeyEnvironmentVariable": "OAS_TEST_KEY",
          "RecentScanDays": 45,
          "RecentScanMaxItems": 5000,
          "ReminderLookAheadHours": 999
        }
        """;
    try
    {
        var settings = RuntimeSettingsSerializer.ParseOrDefault(json);
        var endpoint = settings.ToLlmEndpointSettings();

        Assert(endpoint.CanCall, "Expected callable LLM endpoint.");
        Assert(endpoint.Provider == LlmProviderKind.Ollama, "Expected Ollama provider.");
        Assert(endpoint.ApiKey == "test-token", "Expected API key to resolve from environment variable.");
        Assert(settings.RecentScanDays == 31, "Expected scan days clamp.");
        Assert(settings.RecentScanMaxItems == 2000, "Expected max items clamp.");
        Assert(settings.ReminderLookAheadHours == 24 * 14, "Expected lookahead clamp.");
        return Task.CompletedTask;
    }
    finally
    {
        Environment.SetEnvironmentVariable("OAS_TEST_KEY", null);
    }
}

static async Task LlmJsonCreatesCalendarTask()
{
    var dueAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.FromHours(9));
    var llm = new FakeLlmClient($$"""
        {
          "kind": "calendarEvent",
          "disposition": "autoCreateTask",
          "confidence": 0.93,
          "suggestedTitle": "디자인 리뷰 참석",
          "reason": "일정 참석 요청",
          "evidenceSnippet": "디자인 리뷰 참석 부탁",
          "dueAt": "{{dueAt:O}}",
          "summary": "디자인 리뷰 일정"
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm);
    var result = await analyzer.AnalyzeAsync(Mail("디자인 리뷰", "디자인 리뷰 참석 부탁드립니다."));

    Assert(result.Kind == FollowUpKind.CalendarEvent, "Expected calendar event kind from LLM.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected LLM auto-create disposition.");
    Assert(result.SuggestedTitle == "디자인 리뷰 참석", "Expected Korean title from LLM.");
    Assert(result.DueAt == dueAt, "Expected parsed due date from LLM.");
    Assert(llm.LastUserPayload?.Contains("디자인 리뷰", StringComparison.Ordinal) == true, "Expected mail payload to be sent to LLM client.");
}

static async Task InvalidLlmJsonFallsBackToRules()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(new FakeLlmClient("not-json"));
    var result = await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."));

    Assert(result.Kind == FollowUpKind.Deadline, "Expected fallback rule classification.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected fallback auto task.");
}

static async Task RecentMailScanHonorsRequestWindow()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var source = new SequenceEmailSource(new[]
    {
        Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "scan-1"),
        Mail("공지", "FYI 참고용입니다.", "scan-2"),
        Mail("의견 요청", "다음 주 검토 부탁드립니다.", "scan-3")
    });
    var store = new FakeStore();
    var scanner = new MailActionScanner(source, new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), store));
    var summary = await scanner.ScanAsync(new MailScanRequest(25, IncludeBody: true, now.AddDays(-30)));

    var lastRequest = source.LastRequest;
    Assert(lastRequest is not null, "Expected source request.");
    Assert(lastRequest!.MaxItems == 25, "Expected max items passed to source.");
    Assert(lastRequest.Since == now.AddDays(-30), "Expected recent-month lower bound.");
    Assert(summary.ReadCount == 3, "Expected three read messages.");
    Assert(summary.TaskCreatedCount == 1, "Expected one auto-created task.");
    Assert(summary.ReviewCandidateCount == 1, "Expected one review candidate.");
    Assert(summary.IgnoredCount == 1, "Expected one ignored message.");
}

static Task ReminderPlannerEmitsLookaheadNotifications()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var task = new LocalTaskItem(
        Guid.NewGuid(),
        "보고서 제출",
        now.AddDays(1),
        null,
        0.9,
        "테스트",
        null,
        LocalTaskStatus.Open,
        null,
        now,
        now);

    var due = ReminderPlanner.DueForNotification(new[] { task }, now, TimeSpan.FromHours(25));

    Assert(due.Count == 2, "Expected D-1 and D-day reminders inside lookahead.");
    Assert(due[0].DdayLabel == "D-1", "Expected D-1 label.");
    Assert(due.Any(item => item.ReminderKey.EndsWith(":D-day", StringComparison.Ordinal)), "Expected D-day reminder key.");
    return Task.CompletedTask;
}

static async Task SqliteStoreTruncatesSourceDerivedFields()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var rawEvidence = "비밀본문-" + new string('가', 600);
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            "제목-" + new string('나', 600),
            null,
            StableHash.Create("source-1"),
            0.91,
            "사유-" + new string('다', 600),
            rawEvidence,
            LocalTaskStatus.Open,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await store.SaveTaskAsync(task);
        var saved = (await store.ListOpenTasksAsync()).Single();

        Assert(saved.Title.Length <= EvidencePolicy.MaxEvidenceChars + 1, "Expected task title truncation.");
        Assert(saved.Reason.Length <= EvidencePolicy.MaxEvidenceChars + 1, "Expected task reason truncation.");
        Assert(saved.EvidenceSnippet is not null && saved.EvidenceSnippet.Length <= EvidencePolicy.MaxEvidenceChars + 1, "Expected evidence truncation.");

        var rawDbBytes = await File.ReadAllBytesAsync(dbPath);
        var forbiddenBytes = System.Text.Encoding.UTF8.GetBytes(new string('가', 300));
        Assert(!ContainsSequence(rawDbBytes, forbiddenBytes), "Expected full source-derived evidence absent from DB file.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteDeleteSourceDerivedDataRedactsTaskAndCandidate()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("원문 제목", "원문 본문", "source-redact");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.66,
            "원문 제목 기반 후보",
            "원문 본문 기반 사유",
            "원문 본문 기반 증거",
            null);
        var task = LocalTaskItem.FromAnalysis(mail, analysis with { Disposition = AnalysisDisposition.AutoCreateTask }, DateTimeOffset.UtcNow);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveTaskAsync(task);
        await store.SaveReviewCandidateAsync(candidate);
        await store.DeleteSourceDerivedDataForSourceAsync(mail.SourceHash);

        var saved = (await store.ListOpenTasksAsync()).Single();
        Assert(saved.Title == LocalTaskItem.RedactedTitle, "Expected task title redaction.");
        Assert(saved.Reason == LocalTaskItem.RedactedReason, "Expected task reason redaction.");
        Assert(saved.EvidenceSnippet is null, "Expected task evidence deletion.");
        Assert(saved.SourceDerivedDataDeleted, "Expected task deletion marker.");

        var candidateRow = await QuerySingleRowAsync(dbPath, "SELECT suggested_title, reason, evidence_snippet FROM review_candidates WHERE source_id_hash = $source", ("$source", mail.SourceHash));
        Assert(candidateRow[0] == LocalTaskItem.RedactedTitle, "Expected candidate title redaction.");
        Assert(candidateRow[1] == LocalTaskItem.RedactedReason, "Expected candidate reason redaction.");
        Assert(candidateRow[2] is null, "Expected candidate evidence deletion.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteSchemaAvoidsRawMailColumns()
{
    var (_, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var columnNames = await QueryColumnAsync(dbPath, "SELECT name FROM pragma_table_info('tasks') UNION SELECT name FROM pragma_table_info('review_candidates')");
        var forbidden = columnNames.Where(column =>
            column.Contains("body", StringComparison.OrdinalIgnoreCase)
            || column.Contains("sender", StringComparison.OrdinalIgnoreCase)
            || column.Contains("subject", StringComparison.OrdinalIgnoreCase)
            || column.Contains("entry", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert(forbidden.Length == 0, $"Expected no raw-mail columns, found: {string.Join(", ", forbidden)}.");
    }
    finally
    {
        cleanup();
    }
}

static async Task<(SqliteFollowUpStore Store, string DbPath, Action Cleanup)> CreateTempStoreAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "OutlookAiSecretary.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, "test.db");
    var store = new SqliteFollowUpStore(dbPath);
    await store.InitializeAsync();
    return (store, dbPath, () =>
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort.
        }
    });
}

static async Task<string?[]> QuerySingleRowAsync(string dbPath, string sql, params (string Name, string Value)[] parameters)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    await using var reader = await command.ExecuteReaderAsync();
    Assert(await reader.ReadAsync(), "Expected at least one row.");
    var values = new string?[reader.FieldCount];
    for (var i = 0; i < reader.FieldCount; i++)
    {
        values[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
    }

    return values;
}

static async Task<IReadOnlyList<string>> QueryColumnAsync(string dbPath, string sql)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = sql;
    var values = new List<string>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        values.Add(reader.GetString(0));
    }

    return values;
}

static bool ContainsSequence(byte[] haystack, byte[] needle)
{
    if (needle.Length == 0)
    {
        return true;
    }

    for (var i = 0; i <= haystack.Length - needle.Length; i++)
    {
        var matched = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                matched = false;
                break;
            }
        }

        if (matched)
        {
            return true;
        }
    }

    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FakeStore : IFollowUpStore
{
    public List<LocalTaskItem> Tasks { get; } = [];
    public List<ReviewCandidate> Candidates { get; } = [];
    public HashSet<string> Processed { get; } = [];
    public HashSet<string> NotTaskSources { get; } = [];

    public Task<bool> HasProcessedSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Processed.Contains(sourceIdHash));

    public Task SaveTaskAsync(LocalTaskItem task, CancellationToken cancellationToken = default)
    {
        Tasks.Add(task);
        return Task.CompletedTask;
    }

    public Task SaveReviewCandidateAsync(ReviewCandidate candidate, CancellationToken cancellationToken = default)
    {
        Candidates.Add(candidate);
        return Task.CompletedTask;
    }

    public Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        Processed.Add(sourceIdHash);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalTaskItem>>(Tasks.Where(task => task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed).ToList());

    public Task MarkNotATaskAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        NotTaskSources.Add(sourceIdHash);
        return DeleteSourceDerivedDataForSourceAsync(sourceIdHash, cancellationToken);
    }

    public Task DeleteSourceDerivedDataAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId);
        if (index >= 0)
        {
            Tasks[index] = Tasks[index].DeleteSourceDerivedData(DateTimeOffset.UtcNow);
        }
        return Task.CompletedTask;
    }

    public Task DeleteSourceDerivedDataForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < Tasks.Count; index++)
        {
            if (Tasks[index].SourceIdHash == sourceIdHash)
            {
                Tasks[index] = Tasks[index].DeleteSourceDerivedData(DateTimeOffset.UtcNow);
            }
        }

        for (var index = 0; index < Candidates.Count; index++)
        {
            if (Candidates[index].SourceIdHash == sourceIdHash)
            {
                Candidates[index] = Candidates[index] with
                {
                    Analysis = Candidates[index].Analysis with
                    {
                        SuggestedTitle = LocalTaskItem.RedactedTitle,
                        Reason = LocalTaskItem.RedactedReason,
                        EvidenceSnippet = null
                    }
                };
            }
        }

        return Task.CompletedTask;
    }
}

sealed class FakeLlmClient : ILlmClient
{
    private readonly string _response;

    public FakeLlmClient(string response)
    {
        _response = response;
    }

    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPayload { get; private set; }

    public Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPayload = userPayload;
        return Task.FromResult(_response);
    }
}

sealed class SequenceEmailSource : IEmailSource
{
    private readonly IReadOnlyList<EmailSnapshot> _messages;

    public SequenceEmailSource(IReadOnlyList<EmailSnapshot> messages)
    {
        _messages = messages;
    }

    public MailReadRequest? LastRequest { get; private set; }

    public Task<EmailReadResult> ReadAsync(MailReadRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new EmailReadResult(_messages.Take(request.MaxItems).ToArray(), Array.Empty<MailReadWarning>(), 0));
    }
}
