using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using MailWhere.Core.Analysis;
using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;
using MailWhere.Core.LLM;
using MailWhere.Core.Localization;
using MailWhere.Core.Mail;
using MailWhere.Core.Notifications;
using MailWhere.Core.Pipeline;
using MailWhere.Core.Reminders;
using MailWhere.Core.Scheduling;
using MailWhere.Core.Scanning;
using MailWhere.Core.Storage;
using MailWhere.Storage;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("Korean deadline request creates auto task", KoreanDeadlineRequestCreatesAutoTask),
    ("Meeting request is classified as meeting", MeetingRequestIsClassifiedAsMeeting),
    ("CC action is ignored but CC meeting is kept", CcActionIgnoredButCcMeetingKept),
    ("Unverified recipient action is conservative", UnverifiedRecipientActionIsConservative),
    ("Korean weekday due date parses", KoreanWeekdayDueDateParses),
    ("FYI mail is ignored", FyiMailIsIgnored),
    ("Evidence is truncated", EvidenceIsTruncated),
    ("Forwarded delegation keeps needed context", ForwardedDelegationKeepsNeededContext),
    ("Forwarded context without current request becomes review", ForwardedContextWithoutCurrentRequestBecomesReview),
    ("Reply quoted history does not auto create", ReplyQuotedHistoryDoesNotAutoCreate),
    ("Explicit other assignee is ignored", ExplicitOtherAssigneeIsIgnored),
    ("Explicit self assignee is recognized", ExplicitSelfAssigneeIsRecognized),
    ("Sent promise is classified as my work", SentPromiseIsClassifiedAsMyWork),
    ("Sent request is classified as waiting on them", SentRequestIsClassifiedAsWaitingOnThem),
    ("Follow-up presentation buckets promise and waiting", FollowUpPresentationBucketsPromiseAndWaiting),
    ("Managed mode blocks automatic check before readiness", ManagedModeBlocksWatcherWithoutGate),
    ("Manual readiness is required even if managed mode is false", SmokeGateRequiredEvenIfManagedModeFalse),
    ("Ambiguous mail does not auto create", AmbiguousMailDoesNotAutoCreate),
    ("Pipeline suppresses duplicate source", PipelineSuppressesDuplicateSource),
    ("Pipeline suppresses semantic thread duplicate", PipelineSuppressesSemanticThreadDuplicate),
    ("Manual task can be created", ManualTaskCanBeCreated),
    ("Review candidate ignore persists", ReviewCandidateIgnorePersists),
    ("Notification throttle suppresses repeat alerts", NotificationThrottleSuppressesRepeatAlerts),
    ("Notification throttle supports once per date", NotificationThrottleSupportsOncePerDate),
    ("Diagnostics exporter drops sensitive detail keys", DiagnosticsExporterDropsSensitiveDetailKeys),
    ("Diagnostics exporter sanitizes allowed detail values", DiagnosticsExporterSanitizesAllowedDetailValues),
    ("Runtime diagnostics export includes safe gate codes", RuntimeDiagnosticsExportIncludesSafeGateCodes),
    ("Partial runtime settings keep safe defaults", PartialRuntimeSettingsKeepSafeDefaults),
    ("Runtime settings map Ollama endpoint", RuntimeSettingsMapOllamaEndpoint),
    ("Runtime settings map legacy OpenAI-compatible endpoint", RuntimeSettingsMapLegacyOpenAiCompatibleEndpoint),
    ("Runtime settings map OpenAI Responses endpoint", RuntimeSettingsMapOpenAiResponsesEndpoint),
    ("Runtime settings serialize canonical provider names", RuntimeSettingsSerializeCanonicalProviderNames),
    ("Runtime settings default unlimited recent scan", RuntimeSettingsDefaultUnlimitedRecentScan),
    ("Runtime settings default daily board time", RuntimeSettingsDefaultDailyBoardTime),
    ("Runtime settings default daily board startup delay", RuntimeSettingsDefaultDailyBoardStartupDelay),
    ("Daily board planner schedules next whole hour", DailyBoardPlannerSchedulesNextWholeHour),
    ("Daily board planner waits for startup settling delay", DailyBoardPlannerWaitsForStartupSettlingDelay),
    ("Daily board route options map manual and today brief", DailyBoardRouteOptionsMapManualAndTodayBrief),
    ("Daily board Today brief route includes brief highlights", DailyBoardTodayBriefRouteIncludesBriefHighlights),
    ("Daily board route hides archived and future snooze", DailyBoardRouteHidesArchivedAndFutureSnooze),
    ("Notification action resolver maps daily brief", NotificationActionResolverMapsDailyBrief),
    ("Daily brief notification marks shown after success", DailyBriefNotificationMarksShownAfterSuccess),
    ("Daily brief notification does not mark shown after cancellation", DailyBriefNotificationDoesNotMarkShownAfterCancellation),
    ("Daily brief notification does not mark shown after failure", DailyBriefNotificationDoesNotMarkShownAfterFailure),
    ("Snooze planner computes presets", SnoozePlannerComputesPresets),
    ("Daily brief planner highlights due and hides future snooze", DailyBriefPlannerHighlightsDueAndHidesFutureSnooze),
    ("Task edit request normalizes simple fields", TaskEditRequestNormalizesSimpleFields),
    ("Korean labels use concise product copy", KoreanLabelsUseConciseProductCopy),
    ("LLM JSON creates calendar task", LlmJsonCreatesCalendarTask),
    ("LLM success does not pre-run fallback rules", LlmSuccessDoesNotPreRunFallbackRules),
    ("LLM payload includes thread and owner context", LlmPayloadIncludesThreadAndOwnerContext),
    ("LLM prompt contains triage policy and few shots", LlmPromptContainsTriagePolicyAndFewShots),
    ("LLM quoted history auto create downgrades to review", LlmQuotedHistoryAutoCreateDowngradesToReview),
    ("LLM explicit other assignee is ignored despite auto create", LlmExplicitOtherAssigneeIsIgnoredDespiteAutoCreate),
    ("LLM forwarded context without delegation downgrades to review", LlmForwardedContextWithoutDelegationDowngradesToReview),
    ("Invalid LLM JSON falls back to rules", InvalidLlmJsonFallsBackToRules),
    ("LLM only failure creates review candidate", LlmOnlyFailureCreatesReviewCandidate),
    ("LLM timeout becomes retryable review", LlmTimeoutBecomesRetryableReview),
    ("LLM user cancellation propagates", LlmUserCancellationPropagates),
    ("Batch LLM maps results", BatchLlmMapsResults),
    ("Batch LLM accepts raw array output", BatchLlmAcceptsRawArrayOutput),
    ("Batch LLM tolerates missing final item", BatchLlmToleratesMissingFinalItem),
    ("Batch LLM partial failure uses rule fallback when enabled", BatchLlmPartialFailureUsesRuleFallbackWhenEnabled),
    ("Batch LLM rejects one-based ids", BatchLlmRejectsOneBasedIds),
    ("Batch LLM rejects duplicate ids", BatchLlmRejectsDuplicateIds),
    ("LLM failure review candidate retries after recovery", LlmFailureReviewCandidateRetriesAfterRecovery),
    ("Repeated LLM failure does not duplicate review candidate", RepeatedLlmFailureDoesNotDuplicateReviewCandidate),
    ("LLM endpoint probe validates JSON object", LlmEndpointProbeValidatesJsonObject),
    ("OpenAI Responses client extracts output text", OpenAiResponsesClientExtractsOutputText),
    ("LLM model catalog loads Ollama models", LlmModelCatalogLoadsOllamaModels),
    ("LLM model catalog loads OpenAI-compatible models", LlmModelCatalogLoadsOpenAiCompatibleModels),
    ("Recent mail scan honors request window", RecentMailScanHonorsRequestWindow),
    ("Recent mail scan supports unlimited count", RecentMailScanSupportsUnlimitedCount),
    ("Mail scan reports progress", MailScanReportsProgress),
    ("Reminder planner emits lookahead notifications", ReminderPlannerEmitsLookaheadNotifications),
    ("Reminder planner suppresses future snooze and emits due snooze", ReminderPlannerSuppressesFutureSnoozeAndEmitsDueSnooze),
    ("SQLite store truncates source-derived fields", SqliteStoreTruncatesSourceDerivedFields),
    ("SQLite review candidates can be listed", SqliteReviewCandidatesCanBeListed),
    ("SQLite review candidate can be resolved as task", SqliteReviewCandidateCanBeResolvedAsTask),
    ("SQLite review candidate not-task redacts source metadata", SqliteReviewCandidateNotTaskRedactsSourceMetadata),
    ("SQLite suppress LLM failure redacts source metadata", SqliteSuppressLlmFailureRedactsSourceMetadata),
    ("SQLite double review approval is idempotent", SqliteDoubleReviewApprovalIsIdempotent),
    ("SQLite review candidate snooze hides until due", SqliteReviewCandidateSnoozeHidesUntilDue),
    ("SQLite task dismiss and due update persist", SqliteTaskDismissAndDueUpdatePersist),
    ("SQLite task archive hides from open list", SqliteTaskArchiveHidesFromOpenList),
    ("SQLite task details edit persists", SqliteTaskDetailsEditPersists),
    ("SQLite task complete and snooze persist", SqliteTaskCompleteAndSnoozePersist),
    ("SQLite stale review ignore does not redact approved task", SqliteStaleReviewIgnoreDoesNotRedactApprovedTask),
    ("SQLite migrates pre daily board schema", SqliteMigratesPreDailyBoardSchema),
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

static EmailSnapshot Mail(
    string subject,
    string body,
    string? id = null,
    string? conversationId = null,
    string? mailboxOwner = null,
    MailboxRecipientRole recipientRole = MailboxRecipientRole.Direct,
    string? sender = null) => new(
    id ?? Guid.NewGuid().ToString("N"),
    new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9)),
    sender ?? "tester",
    subject,
    body,
    conversationId,
    mailboxOwner,
    null,
    recipientRole);

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

static async Task CcActionIgnoredButCcMeetingKept()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var action = await analyzer.AnalyzeAsync(Mail(
        "자료 요청",
        "내일까지 비용 자료 검토 후 회신 부탁드립니다.",
        recipientRole: MailboxRecipientRole.Cc));
    var meeting = await analyzer.AnalyzeAsync(Mail(
        "주간 회의",
        "내일 오후 회의 참석 부탁드립니다.",
        recipientRole: MailboxRecipientRole.Cc));

    Assert(action.Disposition == AnalysisDisposition.Ignore, "CC non-meeting action should not create a board task.");
    Assert(meeting.Disposition == AnalysisDisposition.AutoCreateTask, "CC meeting should still appear as schedule.");
    Assert(meeting.Kind == FollowUpKind.Meeting, "Expected meeting kind.");
}

static async Task UnverifiedRecipientActionIsConservative()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var action = await analyzer.AnalyzeAsync(Mail(
        "자료 요청",
        "내일까지 비용 자료 검토 후 회신 부탁드립니다.",
        recipientRole: MailboxRecipientRole.Other));
    var meeting = await analyzer.AnalyzeAsync(Mail(
        "주간 회의",
        "내일 오후 회의 참석 부탁드립니다.",
        recipientRole: MailboxRecipientRole.Other));

    Assert(action.Disposition == AnalysisDisposition.Review, "Unverified non-meeting action should not auto-create as Direct.");
    Assert(meeting.Disposition == AnalysisDisposition.AutoCreateTask, "Unverified meeting should still appear as schedule.");
}

static Task KoreanWeekdayDueDateParses()
{
    var anchor = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9)); // Thursday
    var friday = SimpleDueDateParser.TryParse("이번 주 금요일까지 공유", anchor);
    var nextMonday = SimpleDueDateParser.TryParse("다음 주 월요일 회의", anchor);
    var dayOnly = SimpleDueDateParser.TryParse("20일까지 견적서 공유 부탁드립니다.", anchor);

    Assert(friday == new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)), "Expected this Friday.");
    Assert(nextMonday == new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.FromHours(9)), "Expected next Monday.");
    Assert(dayOnly == new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9)), "Expected current-month day-only deadline.");
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

static async Task ForwardedDelegationKeepsNeededContext()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var mail = Mail(
        "FW: 고객 요청",
        """
        아래 고객 요청 건 내일까지 검토 후 회신 부탁드립니다.

        -----Original Message-----
        From: customer@example.com
        Subject: 사양 변경 요청
        다음 주 적용 전까지 사양 변경 리스크 검토가 필요합니다.
        """);

    var context = MailBodyContextBuilder.Build(mail);
    var result = await analyzer.AnalyzeAsync(mail);

    Assert(context.Kind == MailContextKind.ForwardedDelegation, "Expected forwarded delegation context.");
    Assert(context.ForwardedContext?.Contains("사양 변경", StringComparison.Ordinal) == true, "Expected forwarded context to be retained.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected explicit current delegation to auto-create.");
}

static async Task ForwardedContextWithoutCurrentRequestBecomesReview()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var mail = Mail(
        "FW: 고객 요청",
        """
        -----Original Message-----
        From: customer@example.com
        Subject: 사양 변경 요청
        내일까지 사양 변경 리스크 검토 후 회신 부탁드립니다.
        """);

    var result = await analyzer.AnalyzeAsync(mail);

    Assert(result.Disposition == AnalysisDisposition.Review, "Forward-only context should surface but not auto-create.");
}

static async Task ReplyQuotedHistoryDoesNotAutoCreate()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var mail = Mail(
        "RE: 자료 요청",
        """
        확인했습니다. 감사합니다.

        -----Original Message-----
        From: tester
        Subject: 자료 요청
        내일까지 비용 자료 검토 후 회신 부탁드립니다.
        """);

    var result = await analyzer.AnalyzeAsync(mail);

    Assert(result.Disposition == AnalysisDisposition.Ignore, "Quoted history alone should not surface stale review items.");
}

static async Task ExplicitOtherAssigneeIsIgnored()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail(
        "자료 요청",
        "김철수님 내일까지 비용 자료 검토 후 회신 부탁드립니다.",
        mailboxOwner: "김영희"));

    Assert(result.Disposition == AnalysisDisposition.Ignore, "Explicit other assignee should be ignored.");
    Assert(result.Reason.Contains("다른 사람", StringComparison.Ordinal), "Expected ownership reason.");
}

static async Task ExplicitSelfAssigneeIsRecognized()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail(
        "자료 요청",
        "영희님 내일까지 비용 자료 검토 후 회신 부탁드립니다.",
        mailboxOwner: "김영희 프로"));

    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Explicit self assignee should remain actionable.");
}

static async Task SentPromiseIsClassifiedAsMyWork()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail(
        "수정본 공유",
        "제가 금요일까지 수정본 공유드리겠습니다.",
        mailboxOwner: "김영희",
        sender: "김영희"));

    Assert(result.Kind == FollowUpKind.PromisedByMe, "Expected sent promise to be tracked as my promise.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected confident sent promise to auto-create.");
    Assert(result.DueAt is not null, "Expected due date on promised item.");
    Assert(FollowUpPresentation.CategoryFor(LocalTaskItem.FromAnalysis(Mail("x", "x"), result, DateTimeOffset.UtcNow)) == FollowUpDisplayCategory.ActionForMe, "Promised item should appear under my work.");
}

static async Task SentRequestIsClassifiedAsWaitingOnThem()
{
    var analyzer = new RuleBasedFollowUpAnalyzer();
    var result = await analyzer.AnalyzeAsync(Mail(
        "견적서 요청",
        "20일까지 견적서 공유 부탁드립니다.",
        mailboxOwner: "김영희",
        sender: "김영희"));

    Assert(result.Kind == FollowUpKind.WaitingForReply, "Expected sent request to be tracked as waiting on them.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected sent request to auto-create a waiting item.");
    Assert(result.DueAt is not null, "Expected due date on waiting item.");
}

static Task FollowUpPresentationBucketsPromiseAndWaiting()
{
    var now = DateTimeOffset.UtcNow;
    var promise = new LocalTaskItem(
        Guid.NewGuid(),
        "수정본 공유",
        null,
        null,
        null,
        0.9,
        "테스트",
        null,
        LocalTaskStatus.Open,
        null,
        now,
        now,
        Kind: FollowUpKind.PromisedByMe);
    var waiting = promise with { Id = Guid.NewGuid(), Kind = FollowUpKind.WaitingForReply };

    Assert(FollowUpPresentation.CategoryFor(promise) == FollowUpDisplayCategory.ActionForMe, "Promise should be my work.");
    Assert(FollowUpPresentation.CategoryFor(waiting) == FollowUpDisplayCategory.WaitingOnThem, "Waiting item should be waiting-on-them.");
    Assert(FollowUpPresentation.CompactBadge(FollowUpKind.ReplyRequired) == "할 일", "Reply is not a top-level category.");
    Assert(FollowUpPresentation.CompactBadge(FollowUpKind.CalendarEvent) == "일정", "Calendar should use schedule badge.");
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
    Assert(!result.AutomaticWatcherEnabled, "Automatic mail check should be disabled without readiness.");
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
    Assert(!result.AutomaticWatcherEnabled, "Manual readiness should be unconditional for automatic mail checks.");
    Assert(result.Reasons.Any(reason => reason.Contains("manual mail check", StringComparison.OrdinalIgnoreCase)), "Expected manual readiness reason.");
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

static async Task PipelineSuppressesSemanticThreadDuplicate()
{
    var store = new FakeStore();
    var pipeline = new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), store);
    var firstMail = Mail("RE: RE: 자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "thread-1", "conversation-1");
    var secondMail = Mail("FW: RE: 자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "thread-2", "conversation-1");

    var first = await pipeline.ProcessAsync(firstMail);
    var second = await pipeline.ProcessAsync(secondMail);

    Assert(first.Kind == PipelineOutcomeKind.TaskCreated, "Expected first semantic task.");
    Assert(second.Kind == PipelineOutcomeKind.Duplicate, "Expected semantic duplicate suppression.");
    Assert(store.Tasks.Count == 1, "Expected one task after semantic duplicate.");
    Assert(store.Processed.Contains(secondMail.SourceHash), "Expected duplicate mail source to be marked processed.");
}

static async Task ManualTaskCanBeCreated()
{
    var store = new FakeStore();
    var service = new ManualTaskService(store);
    var task = await service.CreateAsync("CFO 메일 답장");
    Assert(task.SourceIdHash is null, "Manual task should not require source mail.");
    Assert(store.Tasks.Count == 1, "Expected persisted manual task.");
}

static async Task ReviewCandidateIgnorePersists()
{
    var store = new FakeStore();
    var mail = Mail("검토 후보", "검토만 부탁드립니다.", "fake-ignore");
    var candidate = ReviewCandidate.FromAnalysis(
        mail,
        new FollowUpAnalysis(
            FollowUpKind.ReviewNeeded,
            AnalysisDisposition.Review,
            0.5,
            "검토 후보",
            "확인 필요",
            "검토",
            null),
        DateTimeOffset.UtcNow);

    await store.SaveReviewCandidateAsync(candidate);
    var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
    var activeCandidates = await store.ListReviewCandidatesAsync();

    Assert(ignored, "Expected candidate ignore to be recorded.");
    Assert(activeCandidates.Count == 0, "Expected ignored candidate to be hidden.");
    Assert(store.Candidates.Single().Analysis.SuggestedTitle == LocalTaskItem.RedactedTitle, "Expected ignored candidate source-derived title redacted.");
    Assert(store.Candidates.Single().SourceSenderDisplay is null, "Expected ignored candidate sender metadata redacted.");
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

static Task NotificationThrottleSupportsOncePerDate()
{
    var throttle = new NotificationThrottle(TimeSpan.FromMinutes(1));
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    Assert(throttle.ShouldNotifyOncePerDate("due-task", now), "First daily alert should pass.");
    Assert(!throttle.ShouldNotifyOncePerDate("due-task", now.AddHours(2)), "Same-day alert should be suppressed.");
    Assert(throttle.ShouldNotifyOncePerDate("due-task", now.AddDays(1)), "Next-day alert should pass.");
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
    Assert(json.Contains("automatic-check-not-requested"), "Expected safe manual-mode reason code.");
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
    Assert(!snapshot.AutomaticWatcherGate.AutomaticWatcherEnabled, "Partial settings must not bypass manual readiness.");
    Assert(snapshot.AutomaticWatcherGate.Reasons.Any(reason => reason.Contains("manual mail check", StringComparison.OrdinalIgnoreCase)), "Expected manual readiness gate reason.");
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
        Assert(endpoint.Provider == LlmProviderKind.OllamaNative, "Expected Ollama-native provider.");
        Assert(endpoint.ApiKey == "test-token", "Expected API key to resolve from environment variable.");
        Assert(settings.RecentScanDays == 31, "Expected scan days clamp.");
        Assert(settings.RecentScanMaxItems == 5000, "Expected explicit max items to be preserved.");
        Assert(settings.ReminderLookAheadHours == 24 * 14, "Expected lookahead clamp.");
        return Task.CompletedTask;
    }
    finally
    {
        Environment.SetEnvironmentVariable("OAS_TEST_KEY", null);
    }
}

static Task RuntimeSettingsMapLegacyOpenAiCompatibleEndpoint()
{
    var json = """
        {
          "ExternalLlmEnabled": true,
          "LlmProvider": "OpenAiCompatible",
          "LlmEndpoint": "http://localhost:8000",
          "LlmModel": "qwen-local"
        }
        """;
    var settings = RuntimeSettingsSerializer.ParseOrDefault(json);
    Assert(settings.LlmProvider == LlmProviderKind.OpenAiChatCompletions, "Expected legacy OpenAiCompatible to map to Chat Completions.");
    Assert(settings.ToLlmEndpointSettings().CanCall, "Expected legacy OpenAI-compatible endpoint to remain callable.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsMapOpenAiResponsesEndpoint()
{
    var json = """
        {
          "ExternalLlmEnabled": true,
          "LlmProvider": "OpenAiResponses",
          "LlmEndpoint": "http://localhost:8000",
          "LlmModel": "qwen-local"
        }
        """;
    var settings = RuntimeSettingsSerializer.ParseOrDefault(json);
    Assert(settings.LlmProvider == LlmProviderKind.OpenAiResponses, "Expected Responses provider.");
    Assert(settings.ToLlmEndpointSettings().CanCall, "Expected Responses endpoint to be callable.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsSerializeCanonicalProviderNames()
{
    var settings = RuntimeSettings.ManagedSafeDefault with
    {
        ExternalLlmEnabled = true,
        LlmProvider = LlmProviderKind.OpenAiChatCompletions,
        LlmEndpoint = "http://localhost:8000",
        LlmModel = "qwen-local"
    };
    var json = RuntimeSettingsSerializer.Serialize(settings);
    Assert(json.Contains("\"LlmProvider\": \"OpenAiChatCompletions\"", StringComparison.Ordinal), "Expected canonical provider name in saved settings.");
    Assert(!json.Contains("\"OpenAiCompatible\"", StringComparison.Ordinal), "Expected legacy provider alias not to be used when saving settings.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsDefaultUnlimitedRecentScan()
{
    var defaults = RuntimeSettingsSerializer.ParseOrDefault("{}");
    Assert(defaults.RecentScanDays == 30, "Expected recent scan days default.");
    Assert(defaults.RecentScanMaxItems == 0, "Expected default scan max to mean unlimited.");
    Assert(defaults.LlmFallbackPolicy == LlmFallbackPolicy.LlmOnly, "Expected default LLM failure handling to require explicit fallback consent.");
    Assert(defaults.LlmModel.Length == 0, "Expected default LLM model to stay empty until model discovery or user input.");

    var explicitUnlimited = RuntimeSettingsSerializer.ParseOrDefault("""{"RecentScanMaxItems":0,"LlmFallbackPolicy":"LlmThenRules"}""");
    Assert(explicitUnlimited.RecentScanMaxItems == 0, "Expected explicit unlimited scan max.");
    Assert(explicitUnlimited.LlmFallbackPolicy == LlmFallbackPolicy.LlmThenRules, "Expected explicit fallback policy to be preserved.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsDefaultDailyBoardTime()
{
    var defaults = RuntimeSettingsSerializer.ParseOrDefault("{}");
    Assert(defaults.DailyBoardTime == "08:00", "Expected default daily board time.");

    var invalid = RuntimeSettingsSerializer.ParseOrDefault("""{"DailyBoardTime":"not-time"}""");
    Assert(invalid.DailyBoardTime == "08:00", "Expected invalid board time to fall back.");

    var valid = RuntimeSettingsSerializer.ParseOrDefault("""{"DailyBoardTime":"9:30"}""");
    Assert(valid.DailyBoardTime == "09:30", "Expected board time normalization.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsDefaultDailyBoardStartupDelay()
{
    var defaults = RuntimeSettingsSerializer.ParseOrDefault("{}");
    Assert(defaults.DailyBoardStartupDelayMinutes == 10, "Expected default startup settling delay.");

    var invalidLow = RuntimeSettingsSerializer.ParseOrDefault("""{"DailyBoardStartupDelayMinutes":-5}""");
    var invalidHigh = RuntimeSettingsSerializer.ParseOrDefault("""{"DailyBoardStartupDelayMinutes":999}""");
    var valid = RuntimeSettingsSerializer.ParseOrDefault("""{"DailyBoardStartupDelayMinutes":15}""");

    Assert(invalidLow.DailyBoardStartupDelayMinutes == 0, "Expected low delay clamp.");
    Assert(invalidHigh.DailyBoardStartupDelayMinutes == 120, "Expected high delay clamp.");
    Assert(valid.DailyBoardStartupDelayMinutes == 15, "Expected custom startup delay.");
    return Task.CompletedTask;
}

static Task DailyBoardPlannerSchedulesNextWholeHour()
{
    var before = new DateTimeOffset(2026, 5, 15, 7, 30, 0, TimeSpan.FromHours(9));
    var beforePlan = DailyBoardPlanner.Plan(before, "08:00", lastShownDateKey: null);
    Assert(!beforePlan.ShouldShowNow, "Before 08:00 should not show immediately.");
    Assert(beforePlan.NextShowAt == new DateTimeOffset(2026, 5, 15, 8, 0, 0, TimeSpan.FromHours(9)), "Expected 08:00 schedule.");

    var after = new DateTimeOffset(2026, 5, 15, 8, 13, 0, TimeSpan.FromHours(9));
    var afterPlan = DailyBoardPlanner.Plan(after, "08:00", lastShownDateKey: null);
    Assert(!afterPlan.ShouldShowNow, "After 08:00 but not top-of-hour should wait.");
    Assert(afterPlan.NextShowAt == new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)), "Expected next whole hour.");

    var topOfHour = new DateTimeOffset(2026, 5, 15, 9, 0, 30, TimeSpan.FromHours(9));
    var duePlan = DailyBoardPlanner.Plan(topOfHour, "08:00", lastShownDateKey: null);
    Assert(duePlan.ShouldShowNow, "Top-of-hour after 08:00 should show.");

    var customMinute = new DateTimeOffset(2026, 5, 15, 8, 30, 20, TimeSpan.FromHours(9));
    var customPlan = DailyBoardPlanner.Plan(customMinute, "08:30", lastShownDateKey: null);
    Assert(customPlan.ShouldShowNow, "Custom board time should show during its scheduled minute.");

    var alreadyShown = DailyBoardPlanner.Plan(topOfHour, "08:00", DailyBoardPlanner.ToDateKey(topOfHour));
    Assert(!alreadyShown.ShouldShowNow, "Already-shown date should not show again.");
    return Task.CompletedTask;
}

static Task DailyBoardPlannerWaitsForStartupSettlingDelay()
{
    var startedAt = new DateTimeOffset(2026, 5, 15, 8, 0, 0, TimeSpan.FromHours(9));
    var beforeSettled = startedAt.AddMinutes(5);
    var beforePlan = DailyBoardPlanner.Plan(beforeSettled, "08:00", lastShownDateKey: null, appStartedAt: startedAt, startupSettlingDelay: TimeSpan.FromMinutes(10));
    Assert(!beforePlan.ShouldShowNow, "Startup plan should wait for settling delay.");
    Assert(beforePlan.NextShowAt == startedAt.AddMinutes(10), "Expected next show at settled time.");

    var afterSettled = startedAt.AddMinutes(10);
    var afterPlan = DailyBoardPlanner.Plan(afterSettled, "08:00", lastShownDateKey: null, appStartedAt: startedAt, startupSettlingDelay: TimeSpan.FromMinutes(10));
    Assert(afterPlan.ShouldShowNow, "Startup plan should show after settling delay.");
    return Task.CompletedTask;
}

static Task DailyBoardRouteOptionsMapManualAndTodayBrief()
{
    var manual = DailyBoardOpenOptions.ManualAll();
    Assert(manual.Filter == BoardRouteFilter.All, "Generic board route should use All filter.");
    Assert(!manual.ShowBriefSummary, "Generic board route should not show brief summary.");
    Assert(manual.Origin == BoardOrigin.Manual, "Generic board route should record manual origin.");
    Assert(manual.BringToFront, "Generic board route should bring the board forward.");

    var fromToast = DailyBoardOpenOptions.TodayBrief(BoardOrigin.DailyBriefToast);
    Assert(fromToast.Filter == BoardRouteFilter.Today, "Daily Brief route should use Today filter.");
    Assert(fromToast.ShowBriefSummary, "Daily Brief route should show the Today brief summary.");
    Assert(fromToast.Origin == BoardOrigin.DailyBriefToast, "Daily Brief route should preserve toast origin.");
    Assert(fromToast.BringToFront, "Daily Brief route should bring the board forward.");
    return Task.CompletedTask;
}

static Task NotificationActionResolverMapsDailyBrief()
{
    var dailyBrief = NotificationActionResolver.Resolve(UserNotificationKind.DailyBrief);
    Assert(dailyBrief.PrimaryTarget == NotificationPrimaryActionTarget.OpenDailyBoardTodayBrief, "Daily Brief primary action should open Today+brief board route.");

    var reminder = NotificationActionResolver.Resolve(UserNotificationKind.Reminder);
    Assert(reminder.PrimaryTarget == NotificationPrimaryActionTarget.OpenDailyBoard, "Reminder primary action should preserve generic board routing.");

    var scanSummary = NotificationActionResolver.Resolve(UserNotificationKind.ScanSummary);
    Assert(scanSummary.PrimaryTarget == NotificationPrimaryActionTarget.OpenDailyBoard, "Scan summary primary action should preserve generic board routing.");
    Assert(scanSummary.SecondaryTarget == NotificationSecondaryActionTarget.OpenReviewTab, "Scan summary secondary action should preserve review tab routing.");

    var notification = DailyBriefNotificationEmitter.CreateNotification(EmptyBriefSnapshot(), new DailyBoardPlan(true, DateTimeOffset.Now, "2026-05-15", "08:00"));
    Assert(notification.Title == "오늘 브리핑", "Expected concise Daily Brief notification title.");
    Assert(notification.Message.Contains("업무 보드", StringComparison.Ordinal), "Daily Brief notification should keep board-source-of-truth copy.");
    return Task.CompletedTask;
}

static Task DailyBoardTodayBriefRouteIncludesBriefHighlights()
{
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    var dueToday = BriefTask("오늘 마감", FollowUpKind.PromisedByMe, now.AddHours(2), LocalTaskStatus.Open, null, now.AddDays(-1), 0.8);
    var dueSnoozedWaiting = BriefTask("다시 볼 대기", FollowUpKind.WaitingForReply, null, LocalTaskStatus.Snoozed, now.AddMinutes(-1), now.AddDays(-2), 0.8);
    var agedWaiting = BriefTask("오래 기다림", FollowUpKind.WaitingForReply, null, LocalTaskStatus.Open, null, now.AddDays(-4), 0.7);
    var futureTask = BriefTask("다음 주 할 일", FollowUpKind.PromisedByMe, now.AddDays(7), LocalTaskStatus.Open, null, now.AddDays(-1), 0.8);

    var todayBrief = DailyBoardRouteTaskSelector.SelectVisibleTasks(
        new[] { dueToday, dueSnoozedWaiting, agedWaiting, futureTask },
        Array.Empty<ReviewCandidate>(),
        now,
        BoardRouteFilter.Today,
        showBriefSummary: true);

    var todayBriefTitles = todayBrief.Select(task => task.Title).ToHashSet(StringComparer.Ordinal);
    Assert(todayBriefTitles.SetEquals(new[]
    {
        "다시 볼 대기",
        "오래 기다림",
        "오늘 마감"
    }), "Today+brief route should include all brief highlights without unrelated future tasks.");

    var plainToday = DailyBoardRouteTaskSelector.SelectVisibleTasks(
        new[] { dueToday, dueSnoozedWaiting, agedWaiting, futureTask },
        Array.Empty<ReviewCandidate>(),
        now,
        BoardRouteFilter.Today,
        showBriefSummary: false);

    Assert(plainToday.Single().Title == "오늘 마감", "Plain Today filter should keep the existing due-today behavior.");
    return Task.CompletedTask;
}

static Task DailyBoardRouteHidesArchivedAndFutureSnooze()
{
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    var open = BriefTask("보이는 업무", FollowUpKind.ActionRequested, null, LocalTaskStatus.Open, null, now.AddDays(-1), 0.8);
    var archived = BriefTask("보관된 업무", FollowUpKind.ActionRequested, null, LocalTaskStatus.Archived, null, now.AddDays(-1), 0.8);
    var futureSnoozed = BriefTask("내일 다시 볼 업무", FollowUpKind.ActionRequested, null, LocalTaskStatus.Snoozed, now.AddDays(1), now.AddDays(-1), 0.8);
    var dueSnoozed = BriefTask("다시 나타난 업무", FollowUpKind.ActionRequested, null, LocalTaskStatus.Snoozed, now.AddMinutes(-1), now.AddDays(-1), 0.8);

    var visible = DailyBoardRouteTaskSelector.SelectVisibleTasks(
        new[] { open, archived, futureSnoozed, dueSnoozed },
        Array.Empty<ReviewCandidate>(),
        now,
        BoardRouteFilter.All,
        showBriefSummary: false);

    Assert(visible.Select(task => task.Title).OrderBy(title => title, StringComparer.Ordinal).SequenceEqual(new[] { "다시 나타난 업무", "보이는 업무" }), "Primary board should hide archived and future-snoozed tasks.");
    return Task.CompletedTask;
}

static async Task DailyBriefNotificationMarksShownAfterSuccess()
{
    var store = new FakeStore();
    var plan = new DailyBoardPlan(true, DateTimeOffset.Now, "2026-05-15", "08:00");
    var sink = new RecordingNotificationSink();
    await DailyBriefNotificationEmitter.EmitAndMarkShownAsync(sink, store, plan, EmptyBriefSnapshot());

    Assert(store.AppState[DailyBoardPlanner.LastShownDateKey] == "2026-05-15", "Successful Daily Brief emission should mark today's key.");
    Assert(sink.Notifications.Single().Kind == UserNotificationKind.DailyBrief, "Expected Daily Brief notification kind.");
}

static async Task DailyBriefNotificationDoesNotMarkShownAfterCancellation()
{
    var store = new FakeStore();
    var plan = new DailyBoardPlan(true, DateTimeOffset.Now, "2026-05-15", "08:00");
    var sink = new RecordingNotificationSink((_, _) => throw new OperationCanceledException());

    try
    {
        await DailyBriefNotificationEmitter.EmitAndMarkShownAsync(sink, store, plan, EmptyBriefSnapshot());
        Assert(false, "Expected canceled notification emission to propagate.");
    }
    catch (OperationCanceledException)
    {
        Assert(!store.AppState.ContainsKey(DailyBoardPlanner.LastShownDateKey), "Canceled Daily Brief emission must not mark shown.");
    }
}

static async Task DailyBriefNotificationDoesNotMarkShownAfterFailure()
{
    var store = new FakeStore();
    var plan = new DailyBoardPlan(true, DateTimeOffset.Now, "2026-05-15", "08:00");
    var sink = new RecordingNotificationSink((_, _) => throw new InvalidOperationException("toast failed"));

    try
    {
        await DailyBriefNotificationEmitter.EmitAndMarkShownAsync(sink, store, plan, EmptyBriefSnapshot());
        Assert(false, "Expected failed notification emission to propagate.");
    }
    catch (InvalidOperationException)
    {
        Assert(!store.AppState.ContainsKey(DailyBoardPlanner.LastShownDateKey), "Failed Daily Brief emission must not mark shown.");
    }
}

static Task SnoozePlannerComputesPresets()
{
    var morning = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)); // Friday
    var afternoon = new DateTimeOffset(2026, 5, 15, 14, 0, 0, TimeSpan.FromHours(9));

    Assert(SnoozePlanner.Plan(SnoozePreset.TodayAtOnePm, morning) == new DateTimeOffset(2026, 5, 15, 13, 0, 0, TimeSpan.FromHours(9)), "Expected today 1 PM.");
    Assert(SnoozePlanner.Plan(SnoozePreset.TodayAtOnePm, afternoon) == new DateTimeOffset(2026, 5, 16, 13, 0, 0, TimeSpan.FromHours(9)), "Expected next-day 1 PM when today's 1 PM passed.");
    Assert(SnoozePlanner.Plan(SnoozePreset.TomorrowMorning, morning) == new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(9)), "Expected tomorrow morning.");
    Assert(SnoozePlanner.Plan(SnoozePreset.NextMondayMorning, morning) == new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.FromHours(9)), "Expected next Monday morning.");
    return Task.CompletedTask;
}

static Task DailyBriefPlannerHighlightsDueAndHidesFutureSnooze()
{
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    var dueAction = BriefTask("오늘 할 일", FollowUpKind.PromisedByMe, now.AddHours(2), LocalTaskStatus.Open, null, now.AddDays(-1), 0.9);
    var futureSnoozed = BriefTask("내일 다시", FollowUpKind.PromisedByMe, now.AddDays(2), LocalTaskStatus.Snoozed, now.AddDays(1), now.AddDays(-1), 0.9);
    var dueSnoozedWaiting = BriefTask("다시 확인할 대기", FollowUpKind.WaitingForReply, null, LocalTaskStatus.Snoozed, now.AddMinutes(-1), now.AddDays(-2), 0.8);
    var youngWaiting = BriefTask("아직 기다림", FollowUpKind.WaitingForReply, now.AddDays(5), LocalTaskStatus.Open, null, now.AddDays(-1), 0.7);
    var oldWaiting = BriefTask("오래 기다림", FollowUpKind.WaitingForReply, null, LocalTaskStatus.Open, null, now.AddDays(-4), 0.7);
    var candidate = ReviewCandidate.FromAnalysis(
        Mail("후보", "확인 부탁드립니다.", "brief-candidate"),
        new FollowUpAnalysis(FollowUpKind.ReviewNeeded, AnalysisDisposition.Review, 0.5, "검토 후보", "검토 필요", null, null),
        now);

    var brief = DailyBriefPlanner.Build(new[] { dueAction, futureSnoozed, dueSnoozedWaiting, youngWaiting, oldWaiting }, new[] { candidate }, now);

    Assert(brief.ActionItems.Single().Title == "오늘 할 일", "Expected only due action item.");
    Assert(brief.WaitingItems.Select(item => item.Title).OrderBy(title => title, StringComparer.Ordinal).SequenceEqual(new[] { "다시 확인할 대기", "오래 기다림" }), "Expected aged and due-snoozed waiting highlights.");
    Assert(!brief.ActionItems.Concat(brief.WaitingItems).Any(item => item.Title == "내일 다시"), "Future snooze should stay hidden from brief.");
    Assert(!brief.WaitingItems.Any(item => item.Title == "아직 기다림"), "Young waiting item should stay off brief.");
    Assert(brief.HiddenCandidateCount == 1, "Review candidates should be counted but hidden by default.");
    return Task.CompletedTask;
}

static Task TaskEditRequestNormalizesSimpleFields()
{
    var request = TaskEditRequest.Create(
        "  제목을 바로잡기  ",
        FollowUpKind.CalendarEvent,
        new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero));

    Assert(request.Title == "제목을 바로잡기", "Expected trimmed title.");
    Assert(request.Kind == FollowUpKind.Meeting, "Calendar-like items should surface as schedule.");

    var waiting = TaskEditRequest.Create("회신 기다리기", FollowUpKind.WaitingForReply, null);
    Assert(waiting.Kind == FollowUpKind.WaitingForReply, "Waiting category should stay explicit.");

    try
    {
        _ = TaskEditRequest.Create("   ", FollowUpKind.ActionRequested, null);
        Assert(false, "Empty title edits must be rejected.");
    }
    catch (ArgumentException)
    {
        return Task.CompletedTask;
    }

    return Task.CompletedTask;
}

static Task KoreanLabelsUseConciseProductCopy()
{
    Assert(KoreanLabels.Kind(FollowUpKind.ActionRequested) == "할 일", "ActionRequested should not surface as an English label.");
    return Task.CompletedTask;
}

static DailyBriefSnapshot EmptyBriefSnapshot() =>
    new(Array.Empty<LocalTaskItem>(), Array.Empty<LocalTaskItem>(), HiddenCandidateCount: 0);

static LocalTaskItem BriefTask(string title, FollowUpKind kind, DateTimeOffset? dueAt, LocalTaskStatus status, DateTimeOffset? snoozeUntil, DateTimeOffset createdAt, double confidence) =>
    new(
        Guid.NewGuid(),
        title,
        dueAt,
        null,
        null,
        confidence,
        "테스트",
        null,
        status,
        snoozeUntil,
        createdAt,
        createdAt,
        Kind: kind);

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

static async Task LlmSuccessDoesNotPreRunFallbackRules()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "actionRequested",
          "disposition": "review",
          "confidence": 0.71,
          "suggestedTitle": "LLM 우선 후보",
          "reason": "LLM이 먼저 판단",
          "evidenceSnippet": "확인 부탁",
          "dueAt": null,
          "summary": "LLM 성공"
        }
        """);

    var fallback = new ThrowingAnalyzer();
    var analyzer = new LlmBackedFollowUpAnalyzer(llm, fallback, LlmFallbackPolicy.LlmThenRules);
    var result = await analyzer.AnalyzeAsync(Mail("확인", "확인 부탁드립니다."));

    Assert(result.SuggestedTitle == "LLM 우선 후보", "Expected LLM result.");
    Assert(!fallback.Called, "Fallback rules should not run before successful LLM output.");
    Assert(analyzer.GetTelemetrySnapshot().LlmSuccessCount == 1, "Expected LLM success telemetry.");
}

static async Task LlmPayloadIncludesThreadAndOwnerContext()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "actionRequested",
          "disposition": "review",
          "confidence": 0.70,
          "suggestedTitle": "전달 맥락 검토",
          "reason": "전달 메일 확인 요청",
          "evidenceSnippet": "아래 건 확인",
          "dueAt": null,
          "summary": "전달 메일 검토",
          "actionOrigin": "forwardedContext",
          "currentSenderRequested": true,
          "explicitAssignee": null,
          "assignedToMailboxUser": true
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm);

    await analyzer.AnalyzeAsync(Mail(
        "FW: 이슈 확인",
        """
        아래 건 확인 부탁드립니다.

        -----Original Message-----
        From: partner@example.com
        Subject: 이슈 확인
        내일까지 리스크 검토가 필요합니다.
        """,
        conversationId: "llm-conv",
        mailboxOwner: "김영희"));

    Assert(llm.LastUserPayload?.Contains("mailboxOwnerDisplayName", StringComparison.Ordinal) == true, "Expected owner field in LLM payload.");
    Assert(llm.LastUserPayload?.Contains("currentMessage", StringComparison.Ordinal) == true, "Expected current-message field in LLM payload.");
    Assert(llm.LastUserPayload?.Contains("forwardedContext", StringComparison.Ordinal) == true, "Expected forwarded-context field in LLM payload.");
    Assert(llm.LastUserPayload?.Contains("bodyForAnalysis", StringComparison.Ordinal) == false, "Expected payload to avoid duplicate full-body fields.");
    Assert(llm.LastSystemPrompt?.Contains("quotedHistory", StringComparison.Ordinal) == true, "Expected prompt to constrain quoted history.");
}

static async Task LlmPromptContainsTriagePolicyAndFewShots()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "none",
          "disposition": "ignore",
          "confidence": 0.8,
          "suggestedTitle": "",
          "reason": "단순 확인",
          "evidenceSnippet": "확인했습니다",
          "dueAt": null,
          "summary": "후속 조치 없음",
          "actionOrigin": "none",
          "currentSenderRequested": false,
          "explicitAssignee": null,
          "assignedToMailboxUser": true
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm);

    await analyzer.AnalyzeAsync(Mail("RE: 자료 요청", "확인했습니다."));

    var prompt = llm.LastSystemPrompt ?? string.Empty;
    Assert(prompt.Contains("판단 정책", StringComparison.Ordinal), "Expected explicit triage policy in prompt.");
    Assert(prompt.Contains("Few-shot", StringComparison.Ordinal), "Expected few-shot examples in prompt.");
    Assert(prompt.Contains("quotedHistoryPreview만 있는 과거 요청", StringComparison.Ordinal), "Expected stale quoted history policy.");
    Assert(prompt.Contains("다른 사람에게 명시 배정", StringComparison.Ordinal), "Expected explicit other-assignee policy.");
    Assert(prompt.Contains("마감일을 상상하지 마세요", StringComparison.Ordinal), "Expected due-date hallucination guard.");
    Assert(prompt.Contains("promisedByMe", StringComparison.Ordinal), "Expected my-promise kind in prompt schema.");
    Assert(prompt.Contains("waitingForReply", StringComparison.Ordinal), "Expected waiting-on-them kind in prompt schema.");
}

static async Task LlmQuotedHistoryAutoCreateDowngradesToReview()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "deadline",
          "disposition": "autoCreateTask",
          "confidence": 0.93,
          "suggestedTitle": "과거 요청 처리",
          "reason": "과거 인용문 요청",
          "evidenceSnippet": "내일까지 회신",
          "dueAt": null,
          "summary": "과거 요청",
          "actionOrigin": "quotedHistory",
          "currentSenderRequested": false,
          "explicitAssignee": null,
          "assignedToMailboxUser": true
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);

    var result = await analyzer.AnalyzeAsync(Mail(
        "RE: 자료 요청",
        """
        확인했습니다.

        -----Original Message-----
        From: tester
        Subject: 자료 요청
        내일까지 비용 자료 검토 후 회신 부탁드립니다.
        """));

    Assert(result.Disposition == AnalysisDisposition.Review, "Quoted-history-only LLM auto-create must be downgraded to review.");
}

static async Task LlmExplicitOtherAssigneeIsIgnoredDespiteAutoCreate()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "deadline",
          "disposition": "autoCreateTask",
          "confidence": 0.93,
          "suggestedTitle": "비용 자료 검토",
          "reason": "명시 요청",
          "evidenceSnippet": "철수님 내일까지 검토",
          "dueAt": null,
          "summary": "검토 요청",
          "actionOrigin": "currentMessage",
          "currentSenderRequested": true,
          "explicitAssignee": "철수",
          "assignedToMailboxUser": false
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);

    var result = await analyzer.AnalyzeAsync(Mail(
        "자료 요청",
        "김철수님 내일까지 비용 자료 검토 후 회신 부탁드립니다.",
        mailboxOwner: "김영희"));

    Assert(result.Disposition == AnalysisDisposition.Ignore, "Explicit other-assignee requests must be ignored even when LLM returns auto-create.");
}

static async Task LlmForwardedContextWithoutDelegationDowngradesToReview()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "actionRequested",
          "disposition": "autoCreateTask",
          "confidence": 0.86,
          "suggestedTitle": "고객 요청 대응",
          "reason": "전달 맥락",
          "evidenceSnippet": "리스크 검토 필요",
          "dueAt": null,
          "summary": "전달된 요청",
          "actionOrigin": "forwardedContext",
          "currentSenderRequested": false,
          "explicitAssignee": null,
          "assignedToMailboxUser": true
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);

    var result = await analyzer.AnalyzeAsync(Mail(
        "FW: 고객 요청",
        """
        -----Original Message-----
        From: customer@example.com
        Subject: 사양 변경 요청
        내일까지 사양 변경 리스크 검토 후 회신 부탁드립니다.
        """));

    Assert(result.Disposition == AnalysisDisposition.Review, "Forwarded context without current delegation must not auto-create.");
}

static async Task InvalidLlmJsonFallsBackToRules()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(new FakeLlmClient("not-json"));
    var result = await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."));

    Assert(result.Kind == FollowUpKind.Deadline, "Expected fallback rule classification.");
    Assert(result.Disposition == AnalysisDisposition.AutoCreateTask, "Expected fallback auto task.");
}

static async Task LlmOnlyFailureCreatesReviewCandidate()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(new FakeLlmClient("not-json"), new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var result = await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."));

    Assert(result.Disposition == AnalysisDisposition.Review, "LLM-only failure should not silently auto-create from rules.");
    Assert(result.Kind == FollowUpKind.ReviewNeeded, "Expected review-needed failure result.");
    Assert(result.Reason.Contains("LLM 분석 실패", StringComparison.Ordinal), "Expected visible LLM failure reason.");
}

static async Task LlmTimeoutBecomesRetryableReview()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(new ThrowingLlmClient(new TaskCanceledException("timeout")), new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var result = await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."));

    Assert(result.Disposition == AnalysisDisposition.Review, "Timeout should become review instead of throwing.");
    Assert(result.Reason.Contains("LLM 분석 실패(timeout)", StringComparison.Ordinal), "Expected timeout failure code.");
}

static async Task LlmUserCancellationPropagates()
{
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();
    var analyzer = new LlmBackedFollowUpAnalyzer(new ThrowingLlmClient(new OperationCanceledException(cts.Token)), new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);

    var propagated = false;
    try
    {
        await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."), cts.Token);
    }
    catch (OperationCanceledException)
    {
        propagated = true;
    }

    Assert(propagated, "User cancellation must still stop the scan.");
}

static async Task BatchLlmMapsResults()
{
    var dueAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.FromHours(9));
    var llm = new FakeLlmClient($$"""
        {
          "items": [
            {
              "id": "0",
              "kind": "deadline",
              "disposition": "autoCreateTask",
              "confidence": 0.91,
              "suggestedTitle": "자료 회신",
              "reason": "마감 요청",
              "evidenceSnippet": "내일까지 회신",
              "dueAt": "{{dueAt:O}}",
              "summary": "자료 회신 필요",
              "actionOrigin": "currentMessage",
              "currentSenderRequested": true,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            },
            {
              "id": "1",
              "kind": "none",
              "disposition": "ignore",
              "confidence": 0.8,
              "suggestedTitle": "",
              "reason": "공지",
              "evidenceSnippet": "FYI",
              "dueAt": null,
              "summary": "후속 조치 없음",
              "actionOrigin": "none",
              "currentSenderRequested": false,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
          ]
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("자료 요청", "내일까지 회신 부탁드립니다.", "batch-1"),
        Mail("공지", "FYI 참고만 해주세요.", "batch-2")
    });

    Assert(results.Count == 2, "Expected two batch results.");
    Assert(results[0].Disposition == AnalysisDisposition.AutoCreateTask, "Expected first batch result to create task.");
    Assert(results[0].DueAt == dueAt, "Expected due date from batch result.");
    Assert(results[1].Disposition == AnalysisDisposition.Ignore, "Expected second batch result to ignore.");
    var prompt = llm.LastSystemPrompt ?? string.Empty;
    Assert(prompt.Contains("/no_think", StringComparison.Ordinal), "Expected no-think instruction for batch prompt.");
    Assert(prompt.Contains("판단 정책", StringComparison.Ordinal), "Expected explicit triage policy in batch prompt.");
    Assert(prompt.Contains("Few-shot", StringComparison.Ordinal), "Expected few-shot examples in batch prompt.");
    Assert(prompt.Contains("quotedHistoryPreview만 있는 과거 요청", StringComparison.Ordinal), "Expected stale quoted history policy in batch prompt.");
    Assert(prompt.Contains("다른 사람에게 명시 배정", StringComparison.Ordinal), "Expected explicit other-assignee policy in batch prompt.");
    Assert(prompt.Contains("마감일을 상상하지 마세요", StringComparison.Ordinal), "Expected due-date hallucination guard in batch prompt.");
}

static async Task BatchLlmAcceptsRawArrayOutput()
{
    var llm = new FakeLlmClient("""
        [
          {
            "kind": "actionRequested",
            "disposition": "review",
            "confidence": 0.72,
            "suggestedTitle": "자료 확인",
            "reason": "확인 요청",
            "evidenceSnippet": "확인 부탁",
            "dueAt": null,
            "summary": "확인 필요",
            "actionOrigin": "currentMessage",
            "currentSenderRequested": true,
            "explicitAssignee": null,
            "assignedToMailboxUser": true
          },
          {
            "kind": "none",
            "disposition": "ignore",
            "confidence": 0.8,
            "suggestedTitle": "",
            "reason": "참고",
            "evidenceSnippet": "FYI",
            "dueAt": null,
            "summary": "후속 조치 없음",
            "actionOrigin": "none",
            "currentSenderRequested": false,
            "explicitAssignee": null,
            "assignedToMailboxUser": true
          }
        ]
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("확인", "확인 부탁드립니다.", "batch-array-1"),
        Mail("공지", "FYI 참고만 해주세요.", "batch-array-2")
    });

    Assert(results.Count == 2, "Expected two raw-array batch results.");
    Assert(results[0].Disposition == AnalysisDisposition.AutoCreateTask, "Expected direct actionable raw-array result to become task.");
    Assert(results[1].Disposition == AnalysisDisposition.Ignore, "Expected second raw-array result to ignore.");
}

static async Task BatchLlmToleratesMissingFinalItem()
{
    var llm = new FakeLlmClient("""
        {
          "items": [
            {
              "id": "0",
              "kind": "actionRequested",
              "disposition": "autoCreateTask",
              "confidence": 0.72,
              "suggestedTitle": "자료 확인",
              "reason": "확인 요청",
              "evidenceSnippet": "확인 부탁",
              "dueAt": null,
              "summary": "확인 필요",
              "actionOrigin": "currentMessage",
              "currentSenderRequested": true,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
          ]
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("확인 1", "확인 부탁드립니다.", "batch-missing-1"),
        Mail("확인 2", "확인 부탁드립니다.", "batch-missing-2"),
        Mail("확인 3", "확인 부탁드립니다.", "batch-missing-3")
    });

    Assert(results.Count == 3, "Partial batch output must still map to every input.");
    Assert(results[0].Disposition == AnalysisDisposition.AutoCreateTask, "Expected returned item to parse normally.");
    Assert(results[1].IsTransientLlmFailureReview, "Expected missing item placeholder for item 2.");
    Assert(results[2].IsTransientLlmFailureReview, "Expected missing item placeholder for item 3.");
}

static async Task BatchLlmPartialFailureUsesRuleFallbackWhenEnabled()
{
    var llm = new FakeLlmClient("""
        {
          "items": [
            {
              "id": "0",
              "kind": "none",
              "disposition": "ignore",
              "confidence": 0.8,
              "suggestedTitle": "",
              "reason": "공지",
              "evidenceSnippet": "FYI",
              "dueAt": null,
              "summary": "후속 조치 없음",
              "actionOrigin": "none",
              "currentSenderRequested": false,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
          ]
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmThenRules);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("공지", "FYI 참고만 해주세요.", "batch-partial-fallback-1"),
        Mail("자료 요청", "내일까지 비용 자료 검토 후 회신 부탁드립니다.", "batch-partial-fallback-2")
    });
    var telemetry = analyzer.GetTelemetrySnapshot();

    Assert(results.Count == 2, "Expected one result per input.");
    Assert(results[0].Disposition == AnalysisDisposition.Ignore, "Expected returned LLM item to remain intact.");
    Assert(results[1].Disposition == AnalysisDisposition.AutoCreateTask, "Expected missing batch item to use rule fallback when enabled.");
    Assert(telemetry.LlmFailureCount == 1, "Expected missing batch item to count as LLM failure.");
    Assert(telemetry.LlmFallbackCount == 1, "Expected one fallback for the missing batch item.");
}

static async Task BatchLlmRejectsOneBasedIds()
{
    var llm = new FakeLlmClient("""
        {
          "items": [
            {
              "id": "1",
              "kind": "actionRequested",
              "disposition": "autoCreateTask",
              "confidence": 0.8,
              "suggestedTitle": "첫 번째로 보이는 항목",
              "reason": "1-based id는 안전하게 매핑할 수 없음",
              "evidenceSnippet": "확인 부탁",
              "dueAt": null,
              "summary": "확인 필요",
              "actionOrigin": "currentMessage",
              "currentSenderRequested": true,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            },
            {
              "id": "2",
              "kind": "none",
              "disposition": "ignore",
              "confidence": 0.8,
              "suggestedTitle": "",
              "reason": "공지",
              "evidenceSnippet": "FYI",
              "dueAt": null,
              "summary": "후속 조치 없음",
              "actionOrigin": "none",
              "currentSenderRequested": false,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
          ]
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("확인 1", "확인 부탁드립니다.", "batch-one-based-1"),
        Mail("공지 2", "FYI 참고만 해주세요.", "batch-one-based-2")
    });
    var telemetry = analyzer.GetTelemetrySnapshot();

    Assert(results.Count == 2, "Expected two safe placeholders for unsafe one-based ids.");
    Assert(results.All(item => item.IsTransientLlmFailureReview), "One-based ids must not be positionally attached to mail.");
    Assert(telemetry.LlmFailureCount == 2, "Expected unsafe batch ids to count as LLM failures.");
}

static async Task BatchLlmRejectsDuplicateIds()
{
    var llm = new FakeLlmClient("""
        {
          "items": [
            {
              "id": "0",
              "kind": "actionRequested",
              "disposition": "autoCreateTask",
              "confidence": 0.8,
              "suggestedTitle": "첫 번째 항목",
              "reason": "확인 요청",
              "evidenceSnippet": "확인 부탁",
              "dueAt": null,
              "summary": "확인 필요",
              "actionOrigin": "currentMessage",
              "currentSenderRequested": true,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            },
            {
              "id": "0",
              "kind": "actionRequested",
              "disposition": "autoCreateTask",
              "confidence": 0.8,
              "suggestedTitle": "중복 id 항목",
              "reason": "중복 id",
              "evidenceSnippet": "확인 부탁",
              "dueAt": null,
              "summary": "확인 필요",
              "actionOrigin": "currentMessage",
              "currentSenderRequested": true,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
          ]
        }
        """);

    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("확인 1", "확인 부탁드립니다.", "batch-duplicate-1"),
        Mail("확인 2", "확인 부탁드립니다.", "batch-duplicate-2")
    });

    Assert(results.Count == 2, "Duplicate ids should still return one result per input.");
    Assert(results[0].Disposition == AnalysisDisposition.AutoCreateTask, "The ordinally trusted first id=0 item can be used.");
    Assert(results[1].IsTransientLlmFailureReview, "Duplicate id must not be reused for the second mail.");
}

static async Task LlmFailureReviewCandidateRetriesAfterRecovery()
{
    var store = new FakeStore();
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "llm-retry-source");
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(
        LlmFailureAnalysis(mail),
        new FollowUpAnalysis(
            FollowUpKind.Deadline,
            AnalysisDisposition.AutoCreateTask,
            0.92,
            "자료 검토 후 회신",
            "LLM 재분석으로 내 업무 항목을 확인했습니다.",
            "내일까지 검토 후 회신",
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)))),
        store);

    var first = await pipeline.ProcessAsync(mail);
    var second = await pipeline.ProcessAsync(mail);

    Assert(first.Kind == PipelineOutcomeKind.ReviewCandidateCreated, "Expected initial LLM failure to create review candidate.");
    Assert(second.Kind == PipelineOutcomeKind.TaskCreated, "Expected recovered LLM analysis to create task.");
    Assert(store.Tasks.Count == 1, "Expected one recovered task.");
    Assert(store.Candidates.Count == 1, "Expected stale failure candidate to be preserved only as resolved history.");
    Assert(store.Candidates.Single().Suppressed, "Expected stale LLM failure candidate to be suppressed after reanalysis.");
    Assert(store.Processed.Contains(mail.SourceHash), "Expected successfully reanalyzed source to be marked processed.");
}

static async Task RepeatedLlmFailureDoesNotDuplicateReviewCandidate()
{
    var store = new FakeStore();
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "llm-repeat-failure-source");
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(
        LlmFailureAnalysis(mail),
        LlmFailureAnalysis(mail)),
        store);

    var first = await pipeline.ProcessAsync(mail);
    var second = await pipeline.ProcessAsync(mail);

    Assert(first.Kind == PipelineOutcomeKind.ReviewCandidateCreated, "Expected initial LLM failure candidate.");
    Assert(second.Kind == PipelineOutcomeKind.Duplicate, "Expected repeated failure to be deduplicated.");
    Assert(store.Candidates.Count == 1, "Expected one open LLM failure candidate only.");
    Assert(!store.Processed.Contains(mail.SourceHash), "Expected source to remain retryable while only LLM failure exists.");
}

static FollowUpAnalysis LlmFailureAnalysis(EmailSnapshot mail) => new(
    FollowUpKind.ReviewNeeded,
    AnalysisDisposition.Review,
    0.2,
    $"LLM 분석 확인 필요: {mail.Subject}",
    "LLM 분석 실패(invalid-json)로 자동 등록하지 않았습니다.",
    null,
    null,
    "LLM endpoint 상태를 확인한 뒤 다시 스캔하세요.");

static async Task LlmEndpointProbeValidatesJsonObject()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OllamaNative,
        Enabled: true,
        Endpoint: "http://localhost:11434",
        Model: "probe-model",
        ApiKey: null,
        TimeoutSeconds: 5);

    var success = await LlmEndpointProbe.ProbeAsync(settings, new FakeLlmClient("""{"ok":true}"""));
    var invalid = await LlmEndpointProbe.ProbeAsync(settings, new FakeLlmClient("not-json"));

    Assert(success.Success, "Expected valid JSON probe success.");
    Assert(success.Code == "ok", "Expected ok code.");
    Assert(!invalid.Success && invalid.Code == "invalid-json", "Expected invalid JSON probe failure.");
}

static async Task OpenAiResponsesClientExtractsOutputText()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OpenAiResponses,
        Enabled: true,
        Endpoint: "http://localhost:8000/v1",
        Model: "qwen-local",
        ApiKey: null,
        TimeoutSeconds: 5);
    var handler = new StubHttpMessageHandler("""
        {
          "output": [
            {
              "content": [
                {
                  "type": "output_text",
                  "text": "{\"ok\":true}"
                }
              ]
            }
          ]
        }
        """);
    var client = new OpenAiResponsesLlmClient(new HttpClient(handler), settings);

    var result = await client.CompleteJsonAsync("system", "user");

    Assert(result == """{"ok":true}""", "Expected Responses output text to be extracted.");
    Assert(handler.LastRequestUri?.AbsolutePath == "/v1/responses", "Expected Responses endpoint path.");
}

static async Task LlmModelCatalogLoadsOllamaModels()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OllamaNative,
        Enabled: true,
        Endpoint: "http://localhost:11434",
        Model: "catalog",
        ApiKey: null,
        TimeoutSeconds: 5);
    var handler = new StubHttpMessageHandler("""
        {
          "models": [
            { "name": "qwen3.6:latest" },
            { "name": "llama3.2:latest" }
          ]
        }
        """);

    var models = await LlmModelCatalog.FetchAsync(settings, new HttpClient(handler));

    Assert(models.SequenceEqual(new[] { "llama3.2:latest", "qwen3.6:latest" }), "Expected sorted Ollama model names.");
    Assert(handler.LastRequestUri?.AbsolutePath == "/api/tags", "Expected Ollama tags endpoint.");
}

static async Task LlmModelCatalogLoadsOpenAiCompatibleModels()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OpenAiChatCompletions,
        Enabled: true,
        Endpoint: "http://localhost:8000/v1",
        Model: "catalog",
        ApiKey: null,
        TimeoutSeconds: 5);
    var handler = new StubHttpMessageHandler("""
        {
          "object": "list",
          "data": [
            { "id": "qwen-local" },
            { "id": "gpt-oss" }
          ]
        }
        """);

    var models = await LlmModelCatalog.FetchAsync(settings, new HttpClient(handler));

    Assert(models.SequenceEqual(new[] { "gpt-oss", "qwen-local" }), "Expected sorted OpenAI-compatible model IDs.");
    Assert(handler.LastRequestUri?.AbsolutePath == "/v1/models", "Expected OpenAI-compatible models endpoint.");
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
    Assert(summary.TaskCreatedCount == 2, "Expected direct actionable mail to become tasks.");
    Assert(summary.ReviewCandidateCount == 0, "Expected no review candidate for direct actionable mail.");
    Assert(summary.IgnoredCount == 1, "Expected one ignored message.");
}

static async Task RecentMailScanSupportsUnlimitedCount()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var source = new SequenceEmailSource(new[]
    {
        Mail("자료 요청 1", "내일까지 검토 후 회신 부탁드립니다.", "scan-unlimited-1"),
        Mail("자료 요청 2", "내일까지 검토 후 회신 부탁드립니다.", "scan-unlimited-2"),
        Mail("자료 요청 3", "내일까지 검토 후 회신 부탁드립니다.", "scan-unlimited-3")
    });
    var scanner = new MailActionScanner(source, new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), new FakeStore()));
    var summary = await scanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, now.AddDays(-30)));

    Assert(source.LastRequest?.MaxItems == 0, "Expected unlimited marker passed to source.");
    Assert(summary.ReadCount == 3, "Expected all recent messages when MaxItems is zero.");
}

static async Task MailScanReportsProgress()
{
    var source = new SequenceEmailSource(new[]
    {
        Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "scan-progress-1"),
        Mail("공지", "FYI 참고용입니다.", "scan-progress-2")
    });
    var scanner = new MailActionScanner(source, new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), new FakeStore()));
    var progressEvents = new List<MailScanProgress>();

    await scanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, DateTimeOffset.Now.AddDays(-30)), new InlineProgress<MailScanProgress>(progressEvents.Add));

    Assert(progressEvents.Any(item => item.Phase == "reading"), "Expected reading progress.");
    Assert(progressEvents.Any(item => item.Phase == "analyzing" && item.Total == 2), "Expected analyzing progress with total.");
    Assert(progressEvents.Any(item => item.Phase == "completed"), "Expected completed progress.");
}

static Task ReminderPlannerEmitsLookaheadNotifications()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var task = new LocalTaskItem(
        Guid.NewGuid(),
        "보고서 제출",
        now.AddDays(1),
        null,
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

static Task ReminderPlannerSuppressesFutureSnoozeAndEmitsDueSnooze()
{
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    var futureSnoozed = new LocalTaskItem(
        Guid.NewGuid(),
        "아직 표시되지 않음",
        now,
        null,
        null,
        0.9,
        "테스트",
        null,
        LocalTaskStatus.Snoozed,
        now.AddHours(1),
        now.AddDays(-1),
        now.AddDays(-1));
    var dueSnoozed = futureSnoozed with
    {
        Id = Guid.NewGuid(),
        Title = "다시 볼 시간",
        SnoozeUntil = now.AddMinutes(-5)
    };

    var hidden = ReminderPlanner.DueForNotification(new[] { futureSnoozed }, now, TimeSpan.FromHours(24));
    var due = ReminderPlanner.DueForNotification(new[] { dueSnoozed }, now, TimeSpan.FromMinutes(1));

    Assert(hidden.Count == 0, "Future snooze should suppress reminders.");
    Assert(due.Any(item => item.ReminderKey.EndsWith(":snooze-due", StringComparison.Ordinal)), "Due snooze should emit explicit reminder.");
    Assert(due.Any(item => item.ReminderKey.EndsWith(":D-day", StringComparison.Ordinal)), "Due-day item should still emit D-day reminder.");
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
            "source-1",
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

static async Task SqliteReviewCandidatesCanBeListed()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("검토 요청", "금요일까지 가능하면 검토 부탁드립니다.", "review-list");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ReviewNeeded,
            AnalysisDisposition.Review,
            0.52,
            "검토 후보",
            "확신이 낮아 검토 후보에 남깁니다.",
            "검토 부탁",
            null);

        await store.SaveReviewCandidateAsync(ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow));
        var candidates = await store.ListReviewCandidatesAsync();

        Assert(candidates.Count == 1, "Expected one review candidate.");
        Assert(candidates[0].Analysis.SuggestedTitle == "검토 후보", "Expected candidate title.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteReviewCandidateCanBeResolvedAsTask()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("승인 요청", "내일까지 승인 부탁드립니다.", "review-approve");
        var dueAt = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(9));
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.72,
            "승인 요청 처리",
            "검토 후보",
            "승인 부탁",
            dueAt);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var task = await store.ResolveReviewCandidateAsTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
        var openTasks = await store.ListOpenTasksAsync();
        var activeCandidates = await store.ListReviewCandidatesAsync();

        Assert(task is not null, "Expected candidate to resolve into task.");
        Assert(openTasks.Count == 1, "Expected one created task.");
        Assert(openTasks[0].SourceIdHash == mail.SourceHash, "Expected source hash to carry over.");
        Assert(openTasks[0].SourceId == mail.SourceId, "Expected task to keep read-only source id for Outlook open.");
        Assert(openTasks[0].DueAt == dueAt, "Expected due date to carry over.");
        Assert(activeCandidates.Count == 0, "Expected resolved candidate hidden from active list.");

        var candidateRow = await QuerySingleRowAsync(dbPath, "SELECT source_id FROM review_candidates WHERE id = $id", ("$id", candidate.Id.ToString()));
        Assert(candidateRow[0] is null, "Expected resolved candidate source id to be cleared after task creation.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteReviewCandidateNotTaskRedactsSourceMetadata()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("검토 요청", "확인 부탁드립니다.", "review-not-task");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.61,
            "검토 후보",
            "검토 후보",
            "확인 부탁",
            null);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(candidate.Id, DateTimeOffset.UtcNow);

        Assert(ignored, "Expected not-task resolution to be recorded.");
        var row = await QuerySingleRowAsync(dbPath, "SELECT source_id, source_sender_display, source_received_at, source_recipient_role FROM review_candidates WHERE id = $id", ("$id", candidate.Id.ToString()));
        Assert(row[0] is null, "Expected source id deletion.");
        Assert(row[1] is null, "Expected sender deletion.");
        Assert(row[2] is null, "Expected received-at deletion.");
        Assert(row[3] == MailboxRecipientRole.Other.ToString(), "Expected recipient role to become non-specific.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteSuppressLlmFailureRedactsSourceMetadata()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("LLM 실패", "확인 부탁드립니다.", "review-llm-suppress");
        var candidate = ReviewCandidate.FromAnalysis(mail, LlmFailureAnalysis(mail), DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var rows = await store.SuppressOpenLlmFailureReviewCandidatesForSourceAsync(mail.SourceHash, DateTimeOffset.UtcNow, "Recovered");

        Assert(rows == 1, "Expected one suppressed LLM failure candidate.");
        var row = await QuerySingleRowAsync(dbPath, "SELECT source_id, source_sender_display, source_received_at, source_recipient_role FROM review_candidates WHERE id = $id", ("$id", candidate.Id.ToString()));
        Assert(row[0] is null, "Expected source id deletion.");
        Assert(row[1] is null, "Expected sender deletion.");
        Assert(row[2] is null, "Expected received-at deletion.");
        Assert(row[3] == MailboxRecipientRole.Other.ToString(), "Expected recipient role to become non-specific.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteDoubleReviewApprovalIsIdempotent()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("승인 요청", "내일까지 승인 부탁드립니다.", "review-double-approve");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.72,
            "승인 요청 처리",
            "검토 후보",
            "승인 부탁",
            null);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var first = await store.ResolveReviewCandidateAsTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
        var second = await store.ResolveReviewCandidateAsTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
        var openTasks = await store.ListOpenTasksAsync();

        Assert(first is not null, "Expected first approval to create a task.");
        Assert(second is null, "Expected second approval to be a no-op.");
        Assert(openTasks.Count == 1, "Expected only one task after double approval.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteReviewCandidateSnoozeHidesUntilDue()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("검토 요청", "가능하면 검토 부탁드립니다.", "review-snooze");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ReviewNeeded,
            AnalysisDisposition.Review,
            0.61,
            "나중에 볼 후보",
            "검토 후보",
            "검토 부탁",
            null);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var snoozed = await store.SnoozeReviewCandidateAsync(candidate.Id, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow);
        var hidden = await store.ListReviewCandidatesAsync();

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE review_candidates SET snooze_until = $past WHERE id = $id";
            command.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$id", candidate.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        var visibleAgain = await store.ListReviewCandidatesAsync();

        Assert(snoozed, "Expected candidate snooze update.");
        Assert(hidden.Count == 0, "Expected snoozed candidate hidden before due.");
        Assert(visibleAgain.Count == 1, "Expected candidate visible again after snooze time.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteTaskDismissAndDueUpdatePersist()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var first = new LocalTaskItem(
            Guid.NewGuid(),
            "업무보드 삭제 테스트",
            null,
            StableHash.Create("dismiss-source"),
            "dismiss-source",
            0.9,
            "테스트",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        var second = first with { Id = Guid.NewGuid(), Title = "기한 설정 테스트", SourceIdHash = StableHash.Create("due-source"), SourceId = "due-source" };
        await store.SaveTaskAsync(first);
        await store.SaveTaskAsync(second);

        var dueAt = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero);
        var dismissed = await store.DismissTaskAsync(first.Id, now.AddMinutes(1));
        var updated = await store.UpdateTaskDueAtAsync(second.Id, dueAt, now.AddMinutes(2));
        var open = await store.ListOpenTasksAsync();

        Assert(dismissed, "Expected local task dismiss to succeed.");
        Assert(updated, "Expected due update to succeed.");
        Assert(open.Count == 1, "Dismissed task should be hidden from open list.");
        Assert(open[0].Id == second.Id, "Expected remaining task to be the due-updated item.");
        Assert(open[0].DueAt == dueAt, "Expected due date persisted.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteTaskArchiveHidesFromOpenList()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            "보관 테스트",
            null,
            StableHash.Create("archive-source"),
            "archive-source",
            0.9,
            "테스트",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        await store.SaveTaskAsync(task);

        var archived = await store.ArchiveTaskAsync(task.Id, now.AddMinutes(1));
        var open = await store.ListOpenTasksAsync();

        Assert(archived, "Expected archive to succeed.");
        Assert(open.Count == 0, "Archived task should be hidden from open list.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteTaskDetailsEditPersists()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            "원래 제목",
            now.AddDays(1),
            StableHash.Create("edit-source"),
            "edit-source",
            0.9,
            "테스트",
            null,
            LocalTaskStatus.Snoozed,
            now.AddHours(4),
            now,
            now,
            Kind: FollowUpKind.ActionRequested);
        await store.SaveTaskAsync(task);

        var edited = await store.UpdateTaskDetailsAsync(
            task.Id,
            TaskEditRequest.Create("  회의 일정 확인  ", FollowUpKind.Meeting, null),
            now.AddMinutes(1));
        var open = await store.ListOpenTasksAsync();

        Assert(edited is not null, "Expected edit to return updated task.");
        Assert(edited!.Title == "회의 일정 확인", "Expected normalized title.");
        Assert(edited.Kind == FollowUpKind.Meeting, "Expected edited visible category.");
        Assert(edited.DueAt is null, "Expected edit to clear due date.");
        Assert(edited.Status == LocalTaskStatus.Open, "Editing should unsnooze the task.");
        Assert(edited.SnoozeUntil is null, "Editing should clear snooze.");
        Assert(open.Single().Id == task.Id, "Edited task should be visible again.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteTaskCompleteAndSnoozePersist()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var completeTarget = new LocalTaskItem(
            Guid.NewGuid(),
            "완료 테스트",
            null,
            StableHash.Create("complete-source"),
            "complete-source",
            0.9,
            "테스트",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        var snoozeTarget = completeTarget with
        {
            Id = Guid.NewGuid(),
            Title = "나중에 보기 테스트",
            SourceIdHash = StableHash.Create("snooze-source"),
            SourceId = "snooze-source"
        };
        await store.SaveTaskAsync(completeTarget);
        await store.SaveTaskAsync(snoozeTarget);

        var snoozeUntil = now.AddDays(1);
        var completed = await store.CompleteTaskAsync(completeTarget.Id, now.AddMinutes(1));
        var snoozed = await store.SnoozeTaskAsync(snoozeTarget.Id, snoozeUntil, now.AddMinutes(2));
        var open = await store.ListOpenTasksAsync();

        Assert(completed, "Expected complete to succeed.");
        Assert(snoozed, "Expected snooze to succeed.");
        Assert(open.Count == 0, "Completed and future-snoozed tasks should be hidden from open list.");

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET snooze_until = $past WHERE id = $id";
            command.Parameters.AddWithValue("$past", now.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$id", snoozeTarget.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        var dueOpen = await store.ListOpenTasksAsync();
        Assert(dueOpen.Count == 1, "Due snoozed task should return to the open list.");
        Assert(dueOpen[0].Id == snoozeTarget.Id, "Expected snoozed task to return when due.");
        Assert(dueOpen[0].Status == LocalTaskStatus.Snoozed, "Expected snoozed status.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteStaleReviewIgnoreDoesNotRedactApprovedTask()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var mail = Mail("승인 요청", "내일까지 승인 부탁드립니다.", "review-stale-ignore");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.72,
            "승인 요청 처리",
            "검토 후보",
            "승인 부탁",
            null);
        var candidate = ReviewCandidate.FromAnalysis(mail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(candidate);
        var task = await store.ResolveReviewCandidateAsTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
        var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
        var openTasks = await store.ListOpenTasksAsync();

        Assert(task is not null, "Expected candidate to be approved first.");
        Assert(!ignored, "Expected stale ignore to be a no-op.");
        Assert(openTasks.Count == 1, "Expected approved task to remain.");
        Assert(openTasks[0].Title == "승인 요청 처리", "Expected approved task title not to be redacted.");
        Assert(!openTasks[0].SourceDerivedDataDeleted, "Expected approved task source-derived data to remain.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteMigratesPreDailyBoardSchema()
{
    var directory = Path.Combine(Path.GetTempPath(), "MailWhere.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, "legacy.db");
    try
    {
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE review_candidates (
                    id TEXT PRIMARY KEY,
                    source_id_hash TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    confidence REAL NOT NULL,
                    suggested_title TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    evidence_snippet TEXT NULL,
                    due_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    suppressed INTEGER NOT NULL DEFAULT 0
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteFollowUpStore(dbPath);
        await store.InitializeAsync();

        var columns = await QueryColumnAsync(dbPath, "SELECT name FROM pragma_table_info('review_candidates')");
        Assert(columns.Contains("resolved_at"), "Expected migration to add resolved_at.");
        Assert(columns.Contains("resolution"), "Expected migration to add resolution.");
        Assert(columns.Contains("snooze_until"), "Expected migration to add snooze_until.");
        Assert(columns.Contains("source_id"), "Expected migration to add source_id for read-only Outlook open.");

        var indexes = await QueryColumnAsync(dbPath, "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'review_candidates'");
        Assert(indexes.Contains("idx_review_active"), "Expected active review index after migration.");
        Assert(indexes.Contains("idx_review_active_snooze"), "Expected active review snooze index after migration.");
    }
    finally
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort.
        }
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
        Assert(saved.SourceId is null, "Expected task source id deletion.");
        Assert(saved.SourceSenderDisplay is null, "Expected task sender display deletion.");
        Assert(saved.SourceDerivedDataDeleted, "Expected task deletion marker.");

        var candidateRow = await QuerySingleRowAsync(dbPath, "SELECT suggested_title, reason, evidence_snippet, source_id, source_sender_display, source_received_at, source_recipient_role FROM review_candidates WHERE source_id_hash = $source", ("$source", mail.SourceHash));
        Assert(candidateRow[0] == LocalTaskItem.RedactedTitle, "Expected candidate title redaction.");
        Assert(candidateRow[1] == LocalTaskItem.RedactedReason, "Expected candidate reason redaction.");
        Assert(candidateRow[2] is null, "Expected candidate evidence deletion.");
        Assert(candidateRow[3] is null, "Expected candidate source id deletion.");
        Assert(candidateRow[4] is null, "Expected candidate sender display deletion.");
        Assert(candidateRow[5] is null, "Expected candidate received-at deletion.");
        Assert(candidateRow[6] == MailboxRecipientRole.Other.ToString(), "Expected candidate recipient role to become non-specific after redaction.");
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
            || column.Contains("subject", StringComparison.OrdinalIgnoreCase)
            || column.Contains("entry", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert(forbidden.Length == 0, $"Expected no raw body/subject/entry columns, found: {string.Join(", ", forbidden)}.");
    }
    finally
    {
        cleanup();
    }
}

static async Task<(SqliteFollowUpStore Store, string DbPath, Action Cleanup)> CreateTempStoreAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "MailWhere.Tests", Guid.NewGuid().ToString("N"));
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

sealed class FakeStore : IFollowUpStore, IAppStateStore
{
    public List<LocalTaskItem> Tasks { get; } = [];
    public List<ReviewCandidate> Candidates { get; } = [];
    public HashSet<string> Processed { get; } = [];
    public Dictionary<string, string> AppState { get; } = new(StringComparer.Ordinal);

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

    public Task<bool> HasOpenLlmFailureReviewCandidateForSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Candidates.Any(candidate =>
            candidate.SourceIdHash == sourceIdHash
            && !candidate.Suppressed
            && candidate.Analysis.IsTransientLlmFailureReview));

    public Task<int> SuppressOpenLlmFailureReviewCandidatesForSourceAsync(string sourceIdHash, DateTimeOffset now, string resolution, CancellationToken cancellationToken = default)
    {
        var rows = 0;
        for (var index = 0; index < Candidates.Count; index++)
        {
            var candidate = Candidates[index];
            if (candidate.SourceIdHash != sourceIdHash || candidate.Suppressed || !candidate.Analysis.IsTransientLlmFailureReview)
            {
                continue;
            }

            Candidates[index] = candidate with
            {
                Suppressed = true,
                SourceId = null,
                SourceSenderDisplay = null,
                SourceReceivedAt = null,
                SourceRecipientRole = MailboxRecipientRole.Other,
                Analysis = candidate.Analysis with
                {
                    SuggestedTitle = LocalTaskItem.RedactedTitle,
                    Reason = "LLM 재분석으로 검토 후보를 정리했습니다.",
                    EvidenceSnippet = null
                }
            };
            rows++;
        }

        return Task.FromResult(rows);
    }

    public Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        Processed.Add(sourceIdHash);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalTaskItem>>(Tasks.Where(task => FollowUpPresentation.IsVisibleInPrimary(task, DateTimeOffset.UtcNow)).ToList());

    public Task<IReadOnlyList<ReviewCandidate>> ListReviewCandidatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReviewCandidate>>(Candidates
            .Where(candidate => !candidate.Suppressed && (candidate.SnoozeUntil is null || candidate.SnoozeUntil <= DateTimeOffset.UtcNow))
            .ToList());

    public Task<ReviewCandidate?> GetReviewCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReviewCandidate?>(Candidates.FirstOrDefault(candidate =>
            candidate.Id == candidateId
            && !candidate.Suppressed
            && (candidate.SnoozeUntil is null || candidate.SnoozeUntil <= DateTimeOffset.UtcNow)));

    public Task<LocalTaskItem?> ResolveReviewCandidateAsTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Candidates.FindIndex(candidate => candidate.Id == candidateId && !candidate.Suppressed);
        if (index < 0)
        {
            return Task.FromResult<LocalTaskItem?>(null);
        }

        var candidate = Candidates[index];
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            candidate.Analysis.SuggestedTitle,
            candidate.Analysis.DueAt,
            candidate.SourceIdHash,
            candidate.SourceId,
            candidate.Analysis.Confidence,
            candidate.Analysis.Reason,
            candidate.Analysis.EvidenceSnippet,
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: candidate.SourceSenderDisplay,
            SourceReceivedAt: candidate.SourceReceivedAt,
            SourceRecipientRole: candidate.SourceRecipientRole,
            Kind: candidate.Analysis.Kind);
        Tasks.Add(task);
        Candidates[index] = candidate with { Suppressed = true, SourceId = null };
        return Task.FromResult<LocalTaskItem?>(task);
    }

    public Task<bool> SnoozeReviewCandidateAsync(Guid candidateId, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Candidates.FindIndex(candidate => candidate.Id == candidateId && !candidate.Suppressed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Candidates[index] = Candidates[index] with { SnoozeUntil = until };
        return Task.FromResult(true);
    }

    public Task<bool> ResolveReviewCandidateAsNotTaskAsync(Guid candidateId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Candidates.FindIndex(candidate => candidate.Id == candidateId && !candidate.Suppressed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var candidate = Candidates[index];
        Candidates[index] = candidate with
        {
            Suppressed = true,
            SourceId = null,
            SourceSenderDisplay = null,
            SourceReceivedAt = null,
            SourceRecipientRole = MailboxRecipientRole.Other,
            Analysis = candidate.Analysis with
            {
                SuggestedTitle = LocalTaskItem.RedactedTitle,
                Reason = LocalTaskItem.RedactedReason,
                EvidenceSnippet = null
            }
        };
        return Task.FromResult(true);
    }

    public Task<bool> ArchiveTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with { Status = LocalTaskStatus.Archived, SnoozeUntil = null, UpdatedAt = now };
        return Task.FromResult(true);
    }

    public Task<bool> DismissTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with { Status = LocalTaskStatus.Dismissed, UpdatedAt = now };
        return Task.FromResult(true);
    }

    public Task<bool> CompleteTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with { Status = LocalTaskStatus.Done, SnoozeUntil = null, UpdatedAt = now };
        return Task.FromResult(true);
    }

    public Task<bool> SnoozeTaskAsync(Guid taskId, DateTimeOffset until, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with
        {
            Status = LocalTaskStatus.Snoozed,
            SnoozeUntil = until <= now ? now.AddHours(1) : until,
            UpdatedAt = now
        };
        return Task.FromResult(true);
    }

    public Task<bool> UpdateTaskDueAtAsync(Guid taskId, DateTimeOffset dueAt, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with
        {
            DueAt = dueAt,
            Status = Tasks[index].Status == LocalTaskStatus.Snoozed ? LocalTaskStatus.Open : Tasks[index].Status,
            SnoozeUntil = null,
            UpdatedAt = now
        };
        return Task.FromResult(true);
    }

    public Task<LocalTaskItem?> UpdateTaskDetailsAsync(Guid taskId, TaskEditRequest edit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
        if (index < 0)
        {
            return Task.FromResult<LocalTaskItem?>(null);
        }

        var updated = Tasks[index].UpdateDetails(TaskEditRequest.Create(edit.Title, edit.Kind, edit.DueAt), now);
        Tasks[index] = updated;
        return Task.FromResult<LocalTaskItem?>(updated);
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
                    SourceId = null,
                    SourceSenderDisplay = null,
                    SourceReceivedAt = null,
                    SourceRecipientRole = MailboxRecipientRole.Other,
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

    public Task<string?> GetAppStateAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(AppState.TryGetValue(key, out var value) ? value : null);

    public Task SetAppStateAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        AppState[key] = value;
        return Task.CompletedTask;
    }
}

sealed class RecordingNotificationSink : IUserNotificationSink
{
    private readonly Func<UserNotification, CancellationToken, Task> _handler;

    public RecordingNotificationSink(Func<UserNotification, CancellationToken, Task>? handler = null)
    {
        _handler = handler ?? ((_, _) => Task.CompletedTask);
    }

    public List<UserNotification> Notifications { get; } = [];

    public async Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default)
    {
        Notifications.Add(notification);
        await _handler(notification, cancellationToken);
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

sealed class ThrowingLlmClient : ILlmClient
{
    private readonly Exception _exception;

    public ThrowingLlmClient(Exception exception)
    {
        _exception = exception;
    }

    public Task<string> CompleteJsonAsync(string systemPrompt, string userPayload, CancellationToken cancellationToken = default) =>
        Task.FromException<string>(_exception);
}

sealed class ThrowingAnalyzer : IFollowUpAnalyzer
{
    public bool Called { get; private set; }

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        Called = true;
        throw new InvalidOperationException("Fallback should not be called.");
    }
}

sealed class SequenceAnalyzer : IFollowUpAnalyzer
{
    private readonly Queue<FollowUpAnalysis> _analyses;

    public SequenceAnalyzer(params FollowUpAnalysis[] analyses)
    {
        _analyses = new Queue<FollowUpAnalysis>(analyses);
    }

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        if (_analyses.Count == 0)
        {
            throw new InvalidOperationException("No analysis result queued.");
        }

        return Task.FromResult(_analyses.Dequeue());
    }
}

sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;

    public StubHttpMessageHandler(string response)
    {
        _response = response;
    }

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        });
    }
}

sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;

    public InlineProgress(Action<T> onReport)
    {
        _onReport = onReport;
    }

    public void Report(T value) => _onReport(value);
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
        var messages = request.MaxItems <= 0 ? _messages : _messages.Take(request.MaxItems).ToArray();
        return Task.FromResult(new EmailReadResult(messages.ToArray(), Array.Empty<MailReadWarning>(), 0));
    }
}
