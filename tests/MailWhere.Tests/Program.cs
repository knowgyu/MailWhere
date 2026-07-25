using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MailWhere.Cli;
using Microsoft.Data.Sqlite;
using MailWhere.Core.Analysis;
using MailWhere.Core.Capabilities;
using MailWhere.Core.Domain;
using MailWhere.Core.Export;
using MailWhere.Core.LLM;
using MailWhere.Core.Localization;
using MailWhere.Core.Mail;
using MailWhere.Core.Notifications;
using MailWhere.Core.Pipeline;
using MailWhere.Core.Reminders;
using MailWhere.Core.Scheduling;
using MailWhere.Core.Scanning;
using MailWhere.Core.Search;
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
    ("Follow-up presentation strips card scaffolding", FollowUpPresentationStripsCardScaffolding),
    ("Follow-up presentation uses human due labels", FollowUpPresentationUsesHumanDueLabels),
    ("Managed mode blocks automatic check before readiness", ManagedModeBlocksWatcherWithoutGate),
    ("Manual readiness is required even if managed mode is false", SmokeGateRequiredEvenIfManagedModeFalse),
    ("Ambiguous mail does not auto create", AmbiguousMailDoesNotAutoCreate),
    ("Pipeline suppresses duplicate source", PipelineSuppressesDuplicateSource),
    ("Pipeline suppresses semantic thread duplicate", PipelineSuppressesSemanticThreadDuplicate),
    ("Pipeline suppresses semantic review candidate duplicate", PipelineSuppressesSemanticReviewCandidateDuplicate),
    ("Manual task can be created", ManualTaskCanBeCreated),
    ("Review candidate ignore persists", ReviewCandidateIgnorePersists),
    ("Notification throttle suppresses repeat alerts", NotificationThrottleSuppressesRepeatAlerts),
    ("Notification throttle supports once per date", NotificationThrottleSupportsOncePerDate),
    ("Diagnostics exporter drops sensitive detail keys", DiagnosticsExporterDropsSensitiveDetailKeys),
    ("Diagnostics exporter sanitizes allowed detail values", DiagnosticsExporterSanitizesAllowedDetailValues),
    ("Diagnostics exporter allows content-free mirror metrics", DiagnosticsExporterAllowsContentFreeMirrorMetrics),
    ("Mail mirror diagnostics contract names only content-free keys", MailMirrorDiagnosticsContractNamesOnlyContentFreeKeys),
    ("Runtime diagnostics export includes safe gate codes", RuntimeDiagnosticsExportIncludesSafeGateCodes),
    ("Partial runtime settings keep safe defaults", PartialRuntimeSettingsKeepSafeDefaults),
    ("Runtime settings map Ollama endpoint", RuntimeSettingsMapOllamaEndpoint),
    ("Runtime settings direct API key wins without env setup", RuntimeSettingsDirectApiKeyWinsWithoutEnvSetup),
    ("Runtime settings map legacy OpenAI-compatible endpoint", RuntimeSettingsMapLegacyOpenAiCompatibleEndpoint),
    ("Runtime settings map OpenAI Responses endpoint", RuntimeSettingsMapOpenAiResponsesEndpoint),
    ("Runtime settings serialize canonical provider names", RuntimeSettingsSerializeCanonicalProviderNames),
    ("Runtime settings default unlimited recent scan", RuntimeSettingsDefaultUnlimitedRecentScan),
    ("Runtime settings default LLM concurrency", RuntimeSettingsDefaultLlmConcurrency),
    ("Runtime settings clamps LLM concurrency", RuntimeSettingsClampsLlmConcurrency),
    ("Runtime settings simple setting choices map", RuntimeSettingsSimpleSettingChoicesMap),
    ("Startup launch mode maps tray argument", StartupLaunchModeMapsTrayArgument),
    ("Runtime settings default daily board time", RuntimeSettingsDefaultDailyBoardTime),
    ("Runtime settings default daily board startup delay", RuntimeSettingsDefaultDailyBoardStartupDelay),
    ("Daily board planner schedules next whole hour", DailyBoardPlannerSchedulesNextWholeHour),
    ("Daily board planner waits for startup settling delay", DailyBoardPlannerWaitsForStartupSettlingDelay),
    ("Daily board route options map manual and today brief", DailyBoardRouteOptionsMapManualAndTodayBrief),
    ("Daily board week filter uses calendar week", DailyBoardWeekFilterUsesCalendarWeek),
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
    ("LLM payload keeps long content at the bottom", LlmPayloadKeepsLongContentAtTheBottom),
    ("LLM prompt contains triage policy and few shots", LlmPromptContainsTriagePolicyAndFewShots),
    ("LLM quoted history auto create downgrades to review", LlmQuotedHistoryAutoCreateDowngradesToReview),
    ("LLM explicit other assignee is ignored despite auto create", LlmExplicitOtherAssigneeIsIgnoredDespiteAutoCreate),
    ("LLM forwarded context without delegation downgrades to review", LlmForwardedContextWithoutDelegationDowngradesToReview),
    ("Invalid LLM JSON falls back to rules", InvalidLlmJsonFallsBackToRules),
    ("LLM only failure creates review candidate", LlmOnlyFailureCreatesReviewCandidate),
    ("LLM timeout becomes retryable review", LlmTimeoutBecomesRetryableReview),
    ("LLM HTTP failure exposes status code", LlmHttpFailureExposesStatusCode),
    ("LLM scanner batch size is conservative", LlmScannerBatchSizeIsConservative),
    ("LLM user cancellation propagates", LlmUserCancellationPropagates),
    ("Batch LLM maps results", BatchLlmMapsResults),
    ("Batch LLM passes adaptive request options and prompt limits", BatchLlmPassesAdaptiveRequestOptionsAndPromptLimits),
    ("Batch LLM payload keeps content list last", BatchLlmPayloadKeepsContentListLast),
    ("Batch LLM accepts raw array output", BatchLlmAcceptsRawArrayOutput),
    ("Batch LLM tolerates missing final item", BatchLlmToleratesMissingFinalItem),
    ("Batch LLM partial failure uses rule fallback when enabled", BatchLlmPartialFailureUsesRuleFallbackWhenEnabled),
    ("Batch LLM invalid JSON surfaces failure", BatchLlmInvalidJsonSurfacesFailure),
    ("Batch LLM rejects one-based ids", BatchLlmRejectsOneBasedIds),
    ("Batch LLM rejects duplicate ids", BatchLlmRejectsDuplicateIds),
    ("LLM failure review candidate retries after recovery", LlmFailureReviewCandidateRetriesAfterRecovery),
    ("LLM failure retry service reprocesses active candidate", LlmFailureRetryServiceReprocessesActiveCandidate),
    ("LLM failure retry service reports missing source", LlmFailureRetryServiceReportsMissingSource),
    ("LLM failure retry service reports source lookup failure", LlmFailureRetryServiceReportsSourceLookupFailure),
    ("Repeated LLM failure does not duplicate review candidate", RepeatedLlmFailureDoesNotDuplicateReviewCandidate),
    ("LLM endpoint probe validates JSON object", LlmEndpointProbeValidatesJsonObject),
    ("Ollama client records diagnostics and temperature", OllamaClientRecordsDiagnosticsAndTemperature),
    ("Ollama client does not override runner lifetime or context by default", OllamaClientDoesNotOverrideRunnerLifetimeOrContextByDefault),
    ("OpenAI compatible clients honor output token request options", OpenAiCompatibleClientsHonorOutputTokenRequestOptions),
    ("OpenAI Responses client extracts output text", OpenAiResponsesClientExtractsOutputText),
    ("LLM model catalog loads Ollama models", LlmModelCatalogLoadsOllamaModels),
    ("LLM model catalog loads OpenAI-compatible models", LlmModelCatalogLoadsOpenAiCompatibleModels),
    ("Automatic scan window uses full range without cursor", AutomaticScanWindowUsesFullRangeWithoutCursor),
    ("Automatic scan window uses cursor with overlap", AutomaticScanWindowUsesCursorWithOverlap),
    ("Automatic scan window caps stale and invalid cursor", AutomaticScanWindowCapsStaleAndInvalidCursor),
    ("Automatic scan window plans folder deltas independently", AutomaticScanWindowPlansFolderDeltasIndependently),
    ("Pipeline fast filter skips processed sources only", PipelineFastFilterSkipsProcessedSourcesOnly),
    ("Recent mail scan honors request window", RecentMailScanHonorsRequestWindow),
    ("Recent mail scan fast filter hydrates pending sources only", RecentMailScanFastFilterHydratesPendingSourcesOnly),
    ("Recent mail scan records hydration failures", RecentMailScanRecordsHydrationFailures),
    ("Recent mail scan supports unlimited count", RecentMailScanSupportsUnlimitedCount),
    ("Mail scan reports progress", MailScanReportsProgress),
    ("Mail scan adapts batch size by content length", MailScanAdaptsBatchSizeByContentLength),
    ("Mail scan runs prepared LLM batches concurrently", MailScanRunsPreparedLlmBatchesConcurrently),
    ("Mail scan preserves duplicate sources across concurrent batches", MailScanPreservesDuplicateSourcesAcrossConcurrentBatches),
    ("Mail scan cancellation stops concurrent scheduling", MailScanCancellationStopsConcurrentScheduling),
    ("Reminder planner emits lookahead notifications", ReminderPlannerEmitsLookaheadNotifications),
    ("Reminder planner suppresses future snooze and emits due snooze", ReminderPlannerSuppressesFutureSnoozeAndEmitsDueSnooze),
    ("SQLite store truncates source-derived fields", SqliteStoreTruncatesSourceDerivedFields),
    ("SQLite guarded task save is atomic", SqliteGuardedTaskSaveIsAtomic),
    ("SQLite guarded review candidate save is atomic", SqliteGuardedReviewCandidateSaveIsAtomic),
    ("SQLite review candidates can be listed", SqliteReviewCandidatesCanBeListed),
    ("SQLite review candidate can be resolved as task", SqliteReviewCandidateCanBeResolvedAsTask),
    ("SQLite review final actions mark source processed", SqliteReviewFinalActionsMarkSourceProcessed),
    ("SQLite review candidate not-task redacts source metadata", SqliteReviewCandidateNotTaskRedactsSourceMetadata),
    ("SQLite suppress LLM failure redacts source metadata", SqliteSuppressLlmFailureRedactsSourceMetadata),
    ("SQLite double review approval is idempotent", SqliteDoubleReviewApprovalIsIdempotent),
    ("SQLite review candidate snooze hides until due", SqliteReviewCandidateSnoozeHidesUntilDue),
    ("SQLite task dismiss and due update persist", SqliteTaskDismissAndDueUpdatePersist),
    ("SQLite task archive hides from open list", SqliteTaskArchiveHidesFromOpenList),
    ("SQLite task final actions mark source processed", SqliteTaskFinalActionsMarkSourceProcessed),
    ("SQLite archived tasks can be listed and restored", SqliteArchivedTasksCanBeListedAndRestored),
    ("Pipeline records multi-recipient reply progress", PipelineRecordsMultiRecipientReplyProgress),
    ("Pipeline suggests waiting closure from reply", PipelineSuggestsWaitingClosureFromReply),
    ("Pipeline suggests waiting closure from user acknowledgement", PipelineSuggestsWaitingClosureFromUserAcknowledgement),
    ("LLM closure judge can reject weak reply", LlmClosureJudgeCanRejectWeakReply),
    ("Waiting closure keep and archive decisions persist", WaitingClosureKeepAndArchiveDecisionsPersist),
    ("Weekly review summarizes waiting debt", WeeklyReviewSummarizesWaitingDebt),
    ("MailWhere export omits source ids and includes reply progress", MailWhereExportOmitsSourceIdsAndIncludesReplyProgress),
    ("MailWhere CLI manifest and health emit provider envelopes", MailWhereCliManifestAndHealthEmitProviderEnvelopes),
    ("MailWhere CLI missing database returns JSON error without creating files", MailWhereCliMissingDatabaseReturnsJsonErrorWithoutCreatingFiles),
    ("MailWhere CLI read commands emit sanitized schemas", MailWhereCliReadCommandsEmitSanitizedSchemas),
    ("MailWhere CLI search mail is SQLite only and sanitized", MailWhereCliSearchMailIsSqliteOnlyAndSanitized),
    ("MailWhere CLI project references only Core and Storage", MailWhereCliProjectReferencesOnlyCoreAndStorage),
    ("SQLite task details edit persists", SqliteTaskDetailsEditPersists),
    ("SQLite task complete and snooze persist", SqliteTaskCompleteAndSnoozePersist),
    ("SQLite stale review ignore does not redact approved task", SqliteStaleReviewIgnoreDoesNotRedactApprovedTask),
    ("SQLite migrates pre daily board schema", SqliteMigratesPreDailyBoardSchema),
    ("SQLite delete source-derived data redacts task and candidate", SqliteDeleteSourceDerivedDataRedactsTaskAndCandidate),
    ("SQLite schema avoids raw mail columns", SqliteSchemaAvoidsRawMailColumns),
    ("Mail mirror FTS insert update delete rebuild", MailMirrorFtsInsertUpdateDeleteRebuild),
    ("Mail mirror batch checkpoint atomic", MailMirrorBatchCheckpointAtomic),
    ("Mail mirror search filters normalize and short query fallback", MailMirrorSearchFiltersNormalizeAndShortQueryFallback),
    ("Mail mirror preserves task board database", MailMirrorPreservesTaskBoardDatabase),
    ("Mail mirror concurrent searches use serialized reader", MailMirrorConcurrentSearchesUseSerializedReader),
    ("Mail mirror backfill hydrates only new changed checkpoints folders", MailMirrorBackfillHydratesOnlyNewChangedCheckpointsFolders),
    ("Mail mirror backfill cancel resume keeps atomic batches no duplicates", MailMirrorBackfillCancelResumeKeepsAtomicBatchesNoDuplicates),
    ("Mail mirror backfill isolates hydration failures", MailMirrorBackfillIsolatesHydrationFailures),
    ("Mail mirror reconcile deletes unseen and FTS terms", MailMirrorReconcileDeletesUnseenAndFtsTerms),
    ("Mail mirror reconcile handles Inbox to Sent move", MailMirrorReconcileHandlesInboxToSentMove),
    ("Mail mirror interrupted reconcile retains unseen", MailMirrorInterruptedReconcileRetainsUnseen),
    ("Mail mirror warning reconcile retains unseen", MailMirrorWarningReconcileRetainsUnseen),
    ("Mail mirror event hint only wakes missed event recovery", MailMirrorEventHintOnlyWakesMissedEventRecovery)
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
    string? sender = null,
    IReadOnlyList<string>? recipients = null) => new(
    id ?? Guid.NewGuid().ToString("N"),
    new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9)),
    sender ?? "tester",
    subject,
    body,
    conversationId,
    mailboxOwner,
    recipients,
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

static Task FollowUpPresentationStripsCardScaffolding()
{
    Assert(FollowUpPresentation.ActionTitle("메일 확인: 결제 플로우 문구 확인") == "결제 플로우 문구 확인", "Mail-check prefix should not appear on task cards.");
    Assert(FollowUpPresentation.ActionTitle("오늘 회신 · 보안팀 확인 요청") == "보안팀 확인 요청", "Today/reply scaffolding should not appear on task cards.");
    Assert(FollowUpPresentation.ActionTitle("대기 · OAuth 기준 회신") == "OAuth 기준 회신", "Waiting badge text should stay internal.");
    Assert(FollowUpPresentation.ActionTitle("Action required: launch FAQ") == "launch FAQ", "Generic action-required prefix should be stripped.");
    Assert(FollowUpPresentation.ActionTitle("Action required") == "요청 내용 확인", "Bare action-required subject needs a usable Korean fallback.");
    return Task.CompletedTask;
}

static Task FollowUpPresentationUsesHumanDueLabels()
{
    var friday = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    Assert(FollowUpPresentation.HumanDueText(null, friday) == "날짜 없음", "No due date should use product copy.");
    Assert(FollowUpPresentation.HumanDueText(friday.AddHours(6), friday) == "오늘 15:00", "Same-day due should say today.");
    Assert(FollowUpPresentation.HumanDueText(friday.AddDays(1).AddHours(1), friday) == "내일 10:00", "Tomorrow due should say tomorrow.");
    Assert(FollowUpPresentation.HumanDueText(friday.AddDays(2).AddHours(1), friday) == "이번 주 일요일 10:00", "Same calendar week due should use weekday copy.");
    Assert(FollowUpPresentation.HumanDueText(friday.AddDays(10), friday) == "5/25 09:00", "Later due should use compact date.");
    Assert(FollowUpPresentation.HumanSenderText("Customer Success") == "보낸 사람: Customer Success", "Sender should use explicit label.");
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

static async Task PipelineSuppressesSemanticReviewCandidateDuplicate()
{
    var store = new FakeStore();
    var analysis = new FollowUpAnalysis(
        FollowUpKind.ActionRequested,
        AnalysisDisposition.Review,
        0.61,
        "자료 검토",
        "조치 가능성이 있어 검토가 필요합니다.",
        "검토 부탁",
        null);
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(analysis, analysis), store);
    var firstMail = Mail("RE: 자료 검토", "확인 부탁드립니다.", "review-thread-1", "conversation-review-1");
    var secondMail = Mail("FW: 자료 검토", "확인 부탁드립니다.", "review-thread-2", "conversation-review-1");

    var first = await pipeline.ProcessAsync(firstMail);
    var second = await pipeline.ProcessAsync(secondMail);
    var actionSignature = FollowUpActionSignature.Create(firstMail, analysis);

    Assert(first.Kind == PipelineOutcomeKind.ReviewCandidateCreated, "Expected first semantic review candidate.");
    Assert(second.Kind == PipelineOutcomeKind.Duplicate, "Expected duplicate semantic review candidate suppression.");
    Assert(store.Candidates.Count == 1, "Expected one review candidate after semantic duplicate.");
    Assert(store.Processed.Contains(secondMail.SourceHash), "Expected duplicate review mail source to be marked processed.");
    Assert(actionSignature is not null && store.Processed.Contains(actionSignature), "Expected review action signature to be reserved.");
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

static Task DiagnosticsExporterAllowsContentFreeMirrorMetrics()
{
    var report = new CapabilityReport(DateTimeOffset.UtcNow, new[]
    {
        CapabilityProbeResult.Passed(MailMirrorDiagnostics.Fts5ProbeId, "ok", new Dictionary<string, string>
        {
            ["durationMs"] = "12",
            ["p95Ms"] = "345",
            ["batchSize"] = "25",
            ["pageSize"] = "200",
            ["rowCount"] = "1000",
            ["tokenizer"] = "unicode61",
            ["journalMode"] = "wal",
            ["connectionMode"] = "read-only",
            ["operation"] = "fts5-probe",
            ["body"] = "secret body",
            ["subject"] = "secret subject",
            ["entryId"] = "secret-entry",
            ["storeId"] = "secret-store"
        })
    });

    var json = SanitizedDiagnosticsExporter.Export(report);

    Assert(json.Contains("durationMs", StringComparison.Ordinal), "Expected timing metric retained.");
    Assert(json.Contains("p95Ms", StringComparison.Ordinal), "Expected p95 metric retained.");
    Assert(json.Contains("unicode61", StringComparison.Ordinal), "Expected tokenizer code retained.");
    Assert(json.Contains("wal", StringComparison.OrdinalIgnoreCase), "Expected SQLite journal mode retained.");
    Assert(!json.Contains("secret", StringComparison.OrdinalIgnoreCase), "Expected mail identifiers/content removed.");
    return Task.CompletedTask;
}

static Task MailMirrorDiagnosticsContractNamesOnlyContentFreeKeys()
{
    var forbidden = MailMirrorDiagnostics.ForbiddenDetailKeys;
    Assert(forbidden.Contains("body"), "Expected body to be explicitly forbidden.");
    Assert(forbidden.Contains("entryId"), "Expected Outlook EntryID to be explicitly forbidden.");
    Assert(MailMirrorDiagnostics.ContentFreeMetricKeys.Contains("p95Ms"), "Expected p95 baseline metric.");
    Assert(MailMirrorDiagnostics.ContentFreeMetricKeys.Contains("tokenizer"), "Expected tokenizer capability metric.");
    Assert(MailMirrorDiagnostics.ContentFreeMetricKeys.Contains("journalMode"), "Expected SQLite journal metric.");
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
          "RecentScanDays": 999,
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
        Assert(settings.RecentScanDays == 90, "Expected scan days clamp.");
        Assert(settings.RecentScanMaxItems == 5000, "Expected explicit max items to be preserved.");
        Assert(settings.ReminderLookAheadHours == 24 * 14, "Expected lookahead clamp.");
        return Task.CompletedTask;
    }
    finally
    {
        Environment.SetEnvironmentVariable("OAS_TEST_KEY", null);
    }
}

static Task RuntimeSettingsDirectApiKeyWinsWithoutEnvSetup()
{
    Environment.SetEnvironmentVariable("OAS_TEST_KEY", "env-token");
    var json = """
        {
          "ExternalLlmEnabled": true,
          "LlmProvider": "OpenAiResponses",
          "LlmEndpoint": "https://api.openai.com/v1",
          "LlmModel": "gpt-test",
          "LlmApiKey": "direct-token",
          "LlmApiKeyEnvironmentVariable": "OAS_TEST_KEY"
        }
        """;
    try
    {
        var settings = RuntimeSettingsSerializer.ParseOrDefault(json);
        Assert(settings.ToLlmEndpointSettings().ApiKey == "direct-token", "Direct API key should avoid forcing environment setup.");
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
    Assert(defaults.AutomaticScanIntervalMinutes == 15, "Expected automatic scan interval default.");
    Assert(defaults.LlmFallbackPolicy == LlmFallbackPolicy.LlmOnly, "Expected default LLM failure handling to require explicit fallback consent.");
    Assert(defaults.WindowsStartupRequested, "Expected startup tray registration to be requested by default.");
    Assert(defaults.LlmEndpoint.Length == 0, "Expected default LLM endpoint to stay empty until user input.");
    Assert(defaults.LlmModel.Length == 0, "Expected default LLM model to stay empty until model discovery or user input.");

    var explicitUnlimited = RuntimeSettingsSerializer.ParseOrDefault("""{"RecentScanMaxItems":0,"LlmFallbackPolicy":"LlmThenRules"}""");
    Assert(explicitUnlimited.RecentScanMaxItems == 0, "Expected explicit unlimited scan max.");
    Assert(explicitUnlimited.LlmFallbackPolicy == LlmFallbackPolicy.LlmThenRules, "Expected explicit fallback policy to be preserved.");
    Assert(RuntimeSettingsSerializer.ParseOrDefault("""{"AutomaticScanIntervalMinutes":1}""").AutomaticScanIntervalMinutes == 1, "Expected one-minute automatic scan interval to be supported.");
    Assert(RuntimeSettingsSerializer.ParseOrDefault("""{"AutomaticScanIntervalMinutes":0}""").AutomaticScanIntervalMinutes == 1, "Expected automatic scan interval minimum clamp.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsDefaultLlmConcurrency()
{
    var defaults = RuntimeSettingsSerializer.ParseOrDefault("{}");

    Assert(defaults.LlmInitialConcurrency == 1, "Expected default LLM initial concurrency 1.");
    Assert(defaults.LlmMaxConcurrency == 1, "Expected default LLM max concurrency 1.");
    Assert(new MailScanRequest(0, true, DateTimeOffset.UtcNow).EffectiveLlmConcurrency == 1, "Expected scan request default concurrency 1.");

    var json = RuntimeSettingsSerializer.Serialize(defaults);
    Assert(json.Contains("\"LlmInitialConcurrency\": 1", StringComparison.Ordinal), "Expected initial concurrency in serialized settings.");
    Assert(json.Contains("\"LlmMaxConcurrency\": 1", StringComparison.Ordinal), "Expected max concurrency in serialized settings.");

    var legacy = RuntimeSettingsSerializer.ParseOrDefault("""{"LlmInitialConcurrency":2,"LlmMaxConcurrency":4}""");
    Assert(legacy.LlmInitialConcurrency == 1, "Expected legacy persisted default initial concurrency to downgrade to stable default.");
    Assert(legacy.LlmMaxConcurrency == 1, "Expected legacy persisted default max concurrency to downgrade to stable default.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsClampsLlmConcurrency()
{
    var low = RuntimeSettingsSerializer.ParseOrDefault("""{"LlmInitialConcurrency":0,"LlmMaxConcurrency":0}""");
    Assert(low.LlmInitialConcurrency == 1, "Expected low initial concurrency clamp.");
    Assert(low.LlmMaxConcurrency == 1, "Expected low max concurrency clamp.");

    var high = RuntimeSettingsSerializer.ParseOrDefault("""{"LlmInitialConcurrency":99,"LlmMaxConcurrency":99}""");
    Assert(high.LlmInitialConcurrency == 4, "Expected high initial concurrency clamp to the v0.5.0 ceiling.");
    Assert(high.LlmMaxConcurrency == 4, "Expected high max concurrency clamp to the v0.5.0 ceiling.");
    Assert(new MailScanRequest(0, true, DateTimeOffset.UtcNow, high.LlmInitialConcurrency, high.LlmMaxConcurrency).EffectiveLlmConcurrency == 4, "Expected effective concurrency to respect ceiling.");

    var inverted = RuntimeSettingsSerializer.ParseOrDefault("""{"LlmInitialConcurrency":4,"LlmMaxConcurrency":2}""");
    Assert(inverted.LlmInitialConcurrency == 4, "Expected explicit initial concurrency.");
    Assert(inverted.LlmMaxConcurrency == 2, "Max concurrency should preserve the configured lower ceiling.");
    Assert(new MailScanRequest(0, true, DateTimeOffset.UtcNow, inverted.LlmInitialConcurrency, inverted.LlmMaxConcurrency).EffectiveLlmConcurrency == 2, "Effective concurrency must never exceed configured max.");
    return Task.CompletedTask;
}

static Task RuntimeSettingsSimpleSettingChoicesMap()
{
    Assert(RecentMailRangeChoices.NormalizeDays(1) == 1, "Expected one-day range to stay selectable.");
    Assert(RecentMailRangeChoices.NormalizeDays(3) == 7, "Expected short custom range to normalize to 7-day UI choice.");
    Assert(RecentMailRangeChoices.NormalizeDays(20) == 30, "Expected mid custom range to normalize to 30-day UI choice.");
    Assert(RecentMailRangeChoices.NormalizeDays(45) == 90, "Expected long custom range to normalize to 90-day UI choice.");
    Assert(ReminderNotificationChoices.FromLookAheadHours(0) == ReminderNotificationMode.Off, "Expected zero lookahead to mean notifications off.");
    Assert(ReminderNotificationChoices.FromLookAheadHours(1) == ReminderNotificationMode.DueToday, "Expected one-hour lookahead to mean due-today alerts.");
    Assert(ReminderNotificationChoices.ToLookAheadHours(ReminderNotificationMode.DayBefore) == 24, "Expected day-before mode to map to 24 hours.");
    return Task.CompletedTask;
}

static Task StartupLaunchModeMapsTrayArgument()
{
    Assert(StartupLaunchModeResolver.FromArgs(Array.Empty<string>()) == StartupLaunchMode.ShowMainWindow, "Expected direct launch to show the main board.");
    Assert(StartupLaunchModeResolver.FromArgs(new[] { "--tray" }) == StartupLaunchMode.TrayOnly, "Expected --tray to start in tray mode.");
    var command = StartupLaunchModeResolver.BuildTrayStartupCommand(@"C:\Apps\MailWhere.exe");
    Assert(command == "\"C:\\Apps\\MailWhere.exe\" --tray", "Expected startup command to include tray-only flag.");
    Assert(StartupLaunchModeResolver.MatchesExecutable(command, @"C:\Apps\MailWhere.exe"), "Expected startup command parser to match executable path.");
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

static Task DailyBoardWeekFilterUsesCalendarWeek()
{
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)); // Friday
    var sunday = BriefTask("이번 주 일요일", FollowUpKind.ActionRequested, now.AddDays(2), LocalTaskStatus.Open, null, now, 0.8);
    var nextMonday = BriefTask("다음 주 월요일", FollowUpKind.ActionRequested, now.AddDays(3), LocalTaskStatus.Open, null, now, 0.8);
    var noDue = BriefTask("날짜 없음", FollowUpKind.ActionRequested, null, LocalTaskStatus.Open, null, now, 0.8);

    var week = DailyBoardRouteTaskSelector.SelectVisibleTasks(
        new[] { sunday, nextMonday, noDue },
        Array.Empty<ReviewCandidate>(),
        now,
        BoardRouteFilter.Week,
        showBriefSummary: false);

    Assert(week.Single().Title == "이번 주 일요일", "Week filter should mean this calendar week, not the next seven days.");
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
    Assert(KoreanLabels.Disposition(AnalysisDisposition.Review) == "확인 필요", "Review disposition should avoid candidate wording.");
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

static async Task LlmPayloadKeepsLongContentAtTheBottom()
{
    var llm = new FakeLlmClient("""
        {
          "kind": "none",
          "disposition": "ignore",
          "confidence": 0.8,
          "suggestedTitle": "",
          "reason": "확인만 필요",
          "evidenceSnippet": "확인",
          "dueAt": null,
          "summary": "후속 조치 없음",
          "actionOrigin": "none",
          "currentSenderRequested": false,
          "explicitAssignee": null,
          "assignedToMailboxUser": true
        }
        """);
    var analyzer = new LlmBackedFollowUpAnalyzer(llm);

    await analyzer.AnalyzeAsync(Mail(
        "FW: 확인",
        """
        본문 상단 요청입니다.

        -----Original Message-----
        From: partner@example.com
        Subject: 원문
        인용된 과거 본문입니다.
        """,
        conversationId: "cache-shape",
        mailboxOwner: "김영희"));

    var payload = llm.LastUserPayload ?? string.Empty;
    Assert(!payload.Contains("\"now\"", StringComparison.Ordinal), "Expected prompt payload to avoid high-churn now field.");
    Assert(payload.Contains("\"analysisDate\"", StringComparison.Ordinal), "Expected date-level analysis anchor.");
    Assert(payload.Contains("\"timezone\"", StringComparison.Ordinal), "Expected timezone anchor.");
    Assert(payload.IndexOf("\"content\"", StringComparison.Ordinal) > payload.IndexOf("\"contextFlags\"", StringComparison.Ordinal), "Expected long content block after metadata.");
    Assert(payload.IndexOf("\"currentMessage\"", StringComparison.Ordinal) > payload.IndexOf("\"content\"", StringComparison.Ordinal), "Expected current message inside final content block.");

    using var doc = JsonDocument.Parse(payload);
    var root = doc.RootElement;
    Assert(root.TryGetProperty("mail", out var mail), "Expected mail metadata block.");
    Assert(mail.GetProperty("mailboxOwnerDisplayName").GetString() == "김영희", "Expected owner metadata in mail block.");
    Assert(root.TryGetProperty("content", out var content), "Expected final content block.");
    Assert(content.GetProperty("currentMessage").GetString()?.Contains("본문 상단 요청", StringComparison.Ordinal) == true, "Expected current message in content block.");
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
    Assert(prompt.Contains("분류/상태 접두어를 쓰지 마세요", StringComparison.Ordinal), "Expected action-title prefix guard.");
    Assert(prompt.Contains("promisedByMe", StringComparison.Ordinal), "Expected my-promise kind in prompt schema.");
    Assert(prompt.Contains("waitingForReply", StringComparison.Ordinal), "Expected waiting-on-them kind in prompt schema.");
    Assert(!prompt.Contains("summary", StringComparison.Ordinal), "Expected redundant summary output to be removed from prompt schema.");
    Assert(!prompt.Contains("evidenceSnippet", StringComparison.Ordinal), "Expected evidence snippet to be folded into reason.");
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
    var telemetry = analyzer.GetTelemetrySnapshot();

    Assert(result.Disposition == AnalysisDisposition.Review, "Timeout should become review instead of throwing.");
    Assert(result.Reason.Contains("LLM 분석 실패(timeout)", StringComparison.Ordinal), "Expected timeout failure code.");
    Assert(result.Summary?.Contains("응답 시간 초과", StringComparison.Ordinal) == true, "Expected timeout cause in retry summary.");
    Assert(telemetry.ToKoreanSummary().Contains("응답 시간 초과", StringComparison.Ordinal), "Expected telemetry to describe timeout cause.");
}

static async Task LlmHttpFailureExposesStatusCode()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(
        new ThrowingLlmClient(new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests)),
        new RuleBasedFollowUpAnalyzer(),
        LlmFallbackPolicy.LlmOnly);

    var result = await analyzer.AnalyzeAsync(Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다."));
    var telemetry = analyzer.GetTelemetrySnapshot();

    Assert(result.Reason.Contains("LLM 분석 실패(http-429)", StringComparison.Ordinal), "Expected HTTP status code in failure reason.");
    Assert(result.Summary?.Contains("요청 한도", StringComparison.Ordinal) == true, "Expected rate-limit guidance in retry summary.");
    Assert(telemetry.ToKoreanSummary().Contains("요청 한도", StringComparison.Ordinal), "Expected telemetry to explain rate-limit failures.");
}

static Task LlmScannerBatchSizeIsConservative()
{
    var analyzer = new LlmBackedFollowUpAnalyzer(new FakeLlmClient("{}"), new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);
    var pipeline = new FollowUpPipeline(analyzer, new FakeStore());

    Assert(pipeline.PreferredBatchSize == 4, "Expected scanner LLM batches to stay small enough for local models.");
    return Task.CompletedTask;
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

static async Task BatchLlmPassesAdaptiveRequestOptionsAndPromptLimits()
{
    var responseItems = Enumerable.Range(0, 12)
        .Select(index => $$"""
            {
              "id": "{{index}}",
              "kind": "none",
              "disposition": "ignore",
              "confidence": 0.8,
              "suggestedTitle": "",
              "reason": "공지",
              "dueAt": null,
              "actionOrigin": "none",
              "currentSenderRequested": false,
              "explicitAssignee": null,
              "assignedToMailboxUser": true
            }
            """);
    var llm = new FakeLlmClient("""{"items":[""" + string.Join(",", responseItems) + "]}");
    var analyzer = new LlmBackedFollowUpAnalyzer(llm);

    await analyzer.AnalyzeBatchAsync(Enumerable.Range(0, 12)
        .Select(index => Mail($"공지 {index}", "참고만 해주세요.", $"batch-options-{index}"))
        .ToArray());

    Assert(llm.LastRequestOptions?.ContextTokens is null, "Batch requests should inherit Ollama/server context by default.");
    Assert(llm.LastRequestOptions?.MaxOutputTokens == 2176, "Expected adaptive batch output token budget.");

    var prompt = llm.LastSystemPrompt ?? string.Empty;
    Assert(prompt.Contains("suggestedTitle", StringComparison.Ordinal), "Expected title schema.");
    Assert(prompt.Contains("40자", StringComparison.Ordinal), "Expected explicit title length limit.");
    Assert(prompt.Contains("reason", StringComparison.Ordinal), "Expected reason schema.");
    Assert(prompt.Contains("60자", StringComparison.Ordinal), "Expected explicit reason length limit.");
    Assert(prompt.Contains("markdown", StringComparison.OrdinalIgnoreCase), "Expected markdown prohibition.");
    Assert(!prompt.Contains("summary", StringComparison.Ordinal), "Expected redundant summary output to stay removed.");
    Assert(!prompt.Contains("evidenceSnippet", StringComparison.Ordinal), "Expected evidenceSnippet output to stay removed.");
}

static async Task BatchLlmPayloadKeepsContentListLast()
{
    var llm = new FakeLlmClient("""
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
              "dueAt": null,
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
    await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("자료 요청", "첫 번째 메일 본문입니다.", "batch-shape-1"),
        Mail("공지", "두 번째 메일 본문입니다.", "batch-shape-2")
    });

    var payload = llm.LastUserPayload ?? string.Empty;
    Assert(!payload.Contains("\"now\"", StringComparison.Ordinal), "Expected batch payload to avoid high-churn now field.");
    Assert(payload.IndexOf("\"contents\"", StringComparison.Ordinal) > payload.IndexOf("\"items\"", StringComparison.Ordinal), "Expected body contents after metadata items.");
    Assert(payload.IndexOf("\"currentMessage\"", StringComparison.Ordinal) > payload.IndexOf("\"contents\"", StringComparison.Ordinal), "Expected current messages inside final contents block.");

    using var doc = JsonDocument.Parse(payload);
    var root = doc.RootElement;
    var firstItem = root.GetProperty("items")[0];
    Assert(firstItem.TryGetProperty("mail", out _), "Expected metadata item to keep mail block.");
    Assert(!firstItem.TryGetProperty("currentMessage", out _), "Expected metadata item to omit long body fields.");
    var firstContent = root.GetProperty("contents")[0];
    Assert(firstContent.GetProperty("id").GetString() == "0", "Expected content id to match item id.");
    Assert(firstContent.GetProperty("currentMessage").GetString()?.Contains("첫 번째", StringComparison.Ordinal) == true, "Expected first body in final contents.");
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

static async Task BatchLlmInvalidJsonSurfacesFailure()
{
    var llm = new FakeLlmClient("not-json");
    var analyzer = new LlmBackedFollowUpAnalyzer(llm, new RuleBasedFollowUpAnalyzer(), LlmFallbackPolicy.LlmOnly);

    var results = await analyzer.AnalyzeBatchAsync(new[]
    {
        Mail("확인 1", "확인 부탁드립니다.", "invalid-json-1"),
        Mail("공지 2", "FYI입니다.", "invalid-json-2"),
        Mail("회신 3", "내일까지 회신 부탁드립니다.", "invalid-json-3"),
        Mail("공지 4", "참고만 해주세요.", "invalid-json-4")
    });
    var telemetry = analyzer.GetTelemetrySnapshot();

    Assert(results.Count == 4, "Expected one failure result per input.");
    Assert(results.All(item => item.IsTransientLlmFailureReview), "Invalid batch JSON must surface retryable LLM failure reviews.");
    Assert(telemetry.LlmRequestCount == 1, "Expected no split retry masking the invalid JSON evidence.");
    Assert(telemetry.LlmAttemptCount == 4, "Expected failed batch item attempts to be counted.");
    Assert(telemetry.LlmFailureCount == 4, "Expected invalid JSON to count each batch item as LLM failure.");
    Assert(telemetry.LastFailureCode == "invalid-json", "Expected invalid-json failure code to remain visible.");
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

static async Task LlmFailureRetryServiceReprocessesActiveCandidate()
{
    var store = new FakeStore();
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "llm-service-retry-source");
    var candidate = ReviewCandidate.FromAnalysis(mail, LlmFailureAnalysis(mail), DateTimeOffset.UtcNow);
    await store.SaveReviewCandidateAsync(candidate);
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(new FollowUpAnalysis(
            FollowUpKind.Deadline,
            AnalysisDisposition.AutoCreateTask,
            0.91,
            "자료 검토 후 회신",
            "LLM 복구 후 업무로 등록했습니다.",
            "내일까지 검토 후 회신",
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)))),
        store);
    var service = new ReviewCandidateRetryService(
        store,
        pipeline,
        (reviewCandidate, _) => Task.FromResult<EmailSnapshot?>(reviewCandidate.SourceId == mail.SourceId ? mail : null));

    var summary = await service.RetryTransientLlmFailuresAsync();

    Assert(summary.EligibleCount == 1, "Expected one retryable LLM failure candidate.");
    Assert(summary.TaskCreatedCount == 1, "Expected retry to create a recovered task.");
    Assert(summary.MissingSourceCount == 0, "Expected source resolver to provide the original message.");
    Assert(summary.SourceLookupFailureCount == 0, "Expected no source lookup failure when resolver succeeds.");
    Assert(store.Tasks.Count == 1, "Expected recovered task to be saved.");
    Assert(store.Candidates.Single().Suppressed, "Expected stale failure candidate to be suppressed.");
}

static async Task LlmFailureRetryServiceReportsMissingSource()
{
    var store = new FakeStore();
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "llm-service-missing-source");
    var candidate = ReviewCandidate.FromAnalysis(mail, LlmFailureAnalysis(mail), DateTimeOffset.UtcNow);
    await store.SaveReviewCandidateAsync(candidate);
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(new FollowUpAnalysis(
            FollowUpKind.Deadline,
            AnalysisDisposition.AutoCreateTask,
            0.91,
            "자료 검토 후 회신",
            "이 분석은 원본 메일이 없으면 실행되면 안 됩니다.",
            "내일까지 검토 후 회신",
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)))),
        store);
    var service = new ReviewCandidateRetryService(
        store,
        pipeline,
        (_, _) => Task.FromResult<EmailSnapshot?>(null));

    var summary = await service.RetryTransientLlmFailuresAsync();

    Assert(summary.EligibleCount == 1, "Expected one retryable LLM failure candidate.");
    Assert(summary.RetriedCount == 0, "Expected no retry without an original source mail.");
    Assert(summary.MissingSourceCount == 1, "Expected missing source to be reported.");
    Assert(summary.SourceLookupFailureCount == 0, "Expected no lookup failure when resolver returns null.");
    Assert(summary.TaskCreatedCount == 0, "Expected no task to be created from stale failure metadata.");
    Assert(store.Tasks.Count == 0, "Expected no recovered task without source mail.");
    Assert(!store.Candidates.Single().Suppressed, "Expected missing-source candidate to remain active for later retry.");
}

static async Task LlmFailureRetryServiceReportsSourceLookupFailure()
{
    var store = new FakeStore();
    var mail = Mail("자료 요청", "내일까지 검토 후 회신 부탁드립니다.", "llm-service-source-lookup-failure");
    var candidate = ReviewCandidate.FromAnalysis(mail, LlmFailureAnalysis(mail), DateTimeOffset.UtcNow);
    await store.SaveReviewCandidateAsync(candidate);
    var pipeline = new FollowUpPipeline(new SequenceAnalyzer(new FollowUpAnalysis(
            FollowUpKind.Deadline,
            AnalysisDisposition.AutoCreateTask,
            0.91,
            "자료 검토 후 회신",
            "이 분석은 원본 조회가 실패하면 실행되면 안 됩니다.",
            "내일까지 검토 후 회신",
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9)))),
        store);
    var service = new ReviewCandidateRetryService(
        store,
        pipeline,
        (_, _) => throw new InvalidOperationException("source lookup unavailable"));

    var summary = await service.RetryTransientLlmFailuresAsync();

    Assert(summary.EligibleCount == 1, "Expected one retryable LLM failure candidate.");
    Assert(summary.RetriedCount == 0, "Expected no retry when source lookup fails.");
    Assert(summary.MissingSourceCount == 0, "Expected lookup failure to be distinct from missing source.");
    Assert(summary.SourceLookupFailureCount == 1, "Expected source lookup failure to be reported.");
    Assert(summary.TaskCreatedCount == 0, "Expected no task to be created from stale failure metadata.");
    Assert(store.Tasks.Count == 0, "Expected no recovered task when source lookup fails.");
    Assert(!store.Candidates.Single().Suppressed, "Expected lookup-failure candidate to remain active for later retry.");
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

static async Task OllamaClientRecordsDiagnosticsAndTemperature()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OllamaNative,
        Enabled: true,
        Endpoint: "http://localhost:11434",
        Model: "qwen3.6:latest",
        ApiKey: null,
        TimeoutSeconds: 5);
    var handler = new StubHttpMessageHandler("""
        {
          "model": "qwen3.6:latest",
          "message": {
            "role": "assistant",
            "content": "{\"ok\":true}",
            "thinking": ""
          },
          "done": true,
          "total_duration": 12300000000,
          "load_duration": 100000000,
          "prompt_eval_count": 1800,
          "prompt_eval_duration": 3000000000,
          "eval_count": 220,
          "eval_duration": 9000000000
        }
        """);
    var client = new OllamaLlmClient(new HttpClient(handler), settings);

    var completion = await client.CompleteJsonAsync(
        "system",
        "user",
        requestOptions: new LlmRequestOptions(ContextTokens: 32768, MaxOutputTokens: 2048));

    Assert(completion.Content == """{"ok":true}""", "Expected Ollama message content.");
    Assert(completion.Diagnostics is not null, "Expected Ollama diagnostics.");
    Assert(completion.Diagnostics!.TotalDuration?.TotalMilliseconds == 12300, "Expected total duration converted from nanoseconds.");
    Assert(completion.Diagnostics.PromptEvalCount == 1800, "Expected prompt token count.");
    Assert(completion.Diagnostics.EvalCount == 220, "Expected output token count.");
    Assert(completion.Diagnostics.ThinkingCharCount == 0, "Expected empty thinking metadata when think=false.");
    Assert(handler.LastRequestBody is not null, "Expected request body capture.");
    using var request = JsonDocument.Parse(handler.LastRequestBody!);
    Assert(request.RootElement.GetProperty("think").GetBoolean() == false, "Expected Ollama think=false.");
    Assert(Math.Abs(request.RootElement.GetProperty("options").GetProperty("temperature").GetDouble() - 0.1) < 0.0001, "Expected temperature 0.1.");
    Assert(request.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32() == 32768, "Expected per-request num_ctx.");
    Assert(request.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32() == 2048, "Expected adaptive num_predict.");
}

static async Task OllamaClientDoesNotOverrideRunnerLifetimeOrContextByDefault()
{
    var settings = new LlmEndpointSettings(
        LlmProviderKind.OllamaNative,
        Enabled: true,
        Endpoint: "http://localhost:11434",
        Model: "qwen3.6:latest",
        ApiKey: null,
        TimeoutSeconds: 5);
    var handler = new StubHttpMessageHandler("""
        {
          "model": "qwen3.6:latest",
          "message": {
            "role": "assistant",
            "content": "{\"ok\":true}"
          },
          "done": true
        }
        """);
    var client = new OllamaLlmClient(new HttpClient(handler), settings);

    await client.CompleteJsonAsync(
        "system",
        "user",
        requestOptions: new LlmRequestOptions(MaxOutputTokens: 512));

    Assert(handler.LastRequestBody is not null, "Expected request body capture.");
    using var request = JsonDocument.Parse(handler.LastRequestBody!);
    Assert(!request.RootElement.TryGetProperty("keep_alive", out _), "Default Ollama calls should not override existing runner lifetime.");
    var options = request.RootElement.GetProperty("options");
    Assert(!options.TryGetProperty("num_ctx", out _), "Default Ollama calls should not force a different context window.");
    Assert(options.GetProperty("num_predict").GetInt32() == 512, "Expected output cap to remain explicit.");
}

static async Task OpenAiCompatibleClientsHonorOutputTokenRequestOptions()
{
    var chatSettings = new LlmEndpointSettings(
        LlmProviderKind.OpenAiChatCompletions,
        Enabled: true,
        Endpoint: "http://localhost:8000/v1",
        Model: "qwen-local",
        ApiKey: null,
        TimeoutSeconds: 5);
    var chatHandler = new StubHttpMessageHandler("""
        {
          "choices": [
            {
              "message": {
                "content": "{\"ok\":true}"
              }
            }
          ]
        }
        """);
    var chatClient = new OpenAiChatCompletionsLlmClient(new HttpClient(chatHandler), chatSettings);

    await chatClient.CompleteJsonAsync(
        "system",
        "user",
        requestOptions: new LlmRequestOptions(ContextTokens: 32768, MaxOutputTokens: 1536));

    using var chatRequest = JsonDocument.Parse(chatHandler.LastRequestBody ?? "{}");
    Assert(chatRequest.RootElement.GetProperty("max_tokens").GetInt32() == 1536, "Expected Chat Completions max_tokens from request options.");
    Assert(!chatRequest.RootElement.TryGetProperty("num_ctx", out _), "OpenAI-compatible body must not include Ollama context option.");

    var responsesSettings = chatSettings with { Provider = LlmProviderKind.OpenAiResponses };
    var responsesHandler = new StubHttpMessageHandler("""
        {
          "output_text": "{\"ok\":true}"
        }
        """);
    var responsesClient = new OpenAiResponsesLlmClient(new HttpClient(responsesHandler), responsesSettings);

    await responsesClient.CompleteJsonAsync(
        "system",
        "user",
        requestOptions: new LlmRequestOptions(ContextTokens: 32768, MaxOutputTokens: 1792));

    using var responsesRequest = JsonDocument.Parse(responsesHandler.LastRequestBody ?? "{}");
    Assert(responsesRequest.RootElement.GetProperty("max_output_tokens").GetInt32() == 1792, "Expected Responses max_output_tokens from request options.");
    Assert(!responsesRequest.RootElement.TryGetProperty("num_ctx", out _), "Responses body must not include Ollama context option.");
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

    Assert(result.Content == """{"ok":true}""", "Expected Responses output text to be extracted.");
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


static Task AutomaticScanWindowUsesFullRangeWithoutCursor()
{
    var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
    var plan = AutomaticScanWindowPlanner.Plan(now, recentScanDays: 7, lastSuccessfulScanValue: null);

    Assert(plan.Since == now.AddDays(-7), "Expected first automatic scan to use configured catch-up range.");
    Assert(!plan.UsedLastSuccessfulScan, "Expected missing cursor to be reported as full-window scan.");
    return Task.CompletedTask;
}

static Task AutomaticScanWindowUsesCursorWithOverlap()
{
    var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
    var last = now.AddMinutes(-15);
    var plan = AutomaticScanWindowPlanner.Plan(now, recentScanDays: 7, lastSuccessfulScanValue: last.ToString("O"), overlap: TimeSpan.FromMinutes(10));

    Assert(plan.Since == last.AddMinutes(-10), "Expected automatic scan to overlap from the last successful cursor.");
    Assert(plan.UsedLastSuccessfulScan, "Expected recent cursor to be used.");
    return Task.CompletedTask;
}

static Task AutomaticScanWindowCapsStaleAndInvalidCursor()
{
    var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
    var stale = AutomaticScanWindowPlanner.Plan(now, recentScanDays: 7, lastSuccessfulScanValue: now.AddDays(-30).ToString("O"));
    var invalid = AutomaticScanWindowPlanner.Plan(now, recentScanDays: 7, lastSuccessfulScanValue: "not-a-date");
    var future = AutomaticScanWindowPlanner.Plan(now, recentScanDays: 7, lastSuccessfulScanValue: now.AddHours(1).ToString("O"), overlap: TimeSpan.FromMinutes(10));

    Assert(stale.Since == now.AddDays(-7), "Expected stale cursor to be capped by configured catch-up range.");
    Assert(!stale.UsedLastSuccessfulScan, "Expected capped stale cursor to be reported as full-window scan.");
    Assert(invalid.Since == now.AddDays(-7), "Expected invalid cursor to fall back to configured catch-up range.");
    Assert(future.Since == now.AddMinutes(-10), "Expected future cursor to avoid a future-only scan.");
    Assert(!future.UsedLastSuccessfulScan, "Expected future cursor to be treated as unsafe.");
    return Task.CompletedTask;
}

static Task AutomaticScanWindowPlansFolderDeltasIndependently()
{
    var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
    var inboxLast = now.AddMinutes(-20).ToString("O");
    var plan = AutomaticScanWindowPlanner.PlanFolders(
        now,
        recentScanDays: 7,
        lastSuccessfulInboxScanValue: inboxLast,
        lastSuccessfulSentScanValue: null,
        legacyLastSuccessfulScanValue: null,
        overlap: TimeSpan.FromMinutes(10));

    Assert(plan.InboxSince == now.AddMinutes(-30), "Expected inbox to use its recent cursor with overlap.");
    Assert(plan.SentSince == now.AddDays(-7), "Expected sent to fall back to full configured range without its own cursor.");
    Assert(plan.Since == now.AddDays(-7), "Expected aggregate lower bound to be the earliest folder window.");
    Assert(plan.UsedInboxLastSuccessfulScan, "Expected inbox cursor to be marked used.");
    Assert(!plan.UsedSentLastSuccessfulScan, "Expected sent cursor to be marked missing.");
    return Task.CompletedTask;
}

static async Task PipelineFastFilterSkipsProcessedSourcesOnly()
{
    var store = new FakeStore();
    var processed = Mail("이미 처리", "처리된 본문", "fast-processed");
    var duplicate = Mail("중복", "중복 본문", "fast-duplicate");
    var ambiguous = Mail("공지처럼 보임", string.Empty, "fast-ambiguous");
    store.Processed.Add(processed.SourceHash);
    var pipeline = new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), store);

    var result = await pipeline.FastFilterAsync(new[] { processed, duplicate, duplicate, ambiguous });

    Assert(result.DuplicateCount == 2, "Expected processed source and in-batch duplicate to be skipped.");
    Assert(result.PendingEmails.Select(mail => mail.SourceId).SequenceEqual(new[] { duplicate.SourceId, ambiguous.SourceId }), "Expected unprocessed ambiguous mail to remain analyzable.");
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

static async Task RecentMailScanFastFilterHydratesPendingSourcesOnly()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var processed = Mail("이미 처리", string.Empty, "fast-scan-processed") with { Body = null };
    var pending = Mail("자료 요청", string.Empty, "fast-scan-pending") with { Body = null };
    var source = new HydratingSequenceEmailSource(new[] { processed, pending }, new Dictionary<string, EmailSnapshot>
    {
        [pending.SourceId] = pending with { Body = "내일까지 검토 후 회신 부탁드립니다." }
    });
    var store = new FakeStore();
    store.Processed.Add(processed.SourceHash);
    var scanner = new MailActionScanner(source, new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), store));

    var summary = await scanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, now.AddDays(-1), UseFastFilter: true));

    Assert(source.HydrateCalls == 1, "Expected only the unprocessed pending source to be hydrated.");
    Assert(summary.ReadCount == 2, "Expected metadata read count to include processed and pending messages.");
    Assert(summary.DuplicateCount == 1, "Expected processed source to be counted as duplicate.");
    Assert(summary.TaskCreatedCount == 1, "Expected hydrated pending body to become a task.");
}

static async Task RecentMailScanRecordsHydrationFailures()
{
    var now = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.FromHours(9));
    var pending = Mail("자료 요청", "제목으로라도 확인 부탁드립니다.", "fast-scan-hydration-failure") with { Body = null };
    var source = new HydratingSequenceEmailSource(
        new[] { pending },
        new Dictionary<string, EmailSnapshot>(),
        new HashSet<string> { pending.SourceId });
    var scanner = new MailActionScanner(source, new FollowUpPipeline(new RuleBasedFollowUpAnalyzer(), new FakeStore()));

    var summary = await scanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, now.AddDays(-1), UseFastFilter: true));

    Assert(source.HydrateCalls == 1, "Expected the pending metadata row to be hydrated once.");
    Assert(summary.Warnings.Any(warning => warning.Code == "mail-fast-filter-hydration-failed"), "Expected hydration failure evidence to be preserved as a warning.");
    Assert(summary.TaskCreatedCount + summary.ReviewCandidateCount + summary.IgnoredCount == 1, "Expected scan to continue with metadata after hydration failure.");
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

static async Task MailScanAdaptsBatchSizeByContentLength()
{
    var shortMessages = Enumerable.Range(0, 18)
        .Select(index => Mail($"짧은 요청 {index}", "확인 부탁드립니다.", $"adaptive-short-{index}"))
        .ToArray();
    var shortAnalyzer = new RecordingBatchAnalyzer(12);
    var shortScanner = new MailActionScanner(
        new SequenceEmailSource(shortMessages),
        new FollowUpPipeline(shortAnalyzer, new FakeStore()));

    await shortScanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, DateTimeOffset.Now.AddDays(-30)));

    Assert(shortAnalyzer.BatchSizes.SequenceEqual(new[] { 12, 6 }), "Short messages should use expanded batches up to analyzer preference.");

    var longBody = new string('가', 5000);
    var mixedMessages = new[]
    {
        Mail("긴 요청 1", longBody, "adaptive-long-1"),
        Mail("긴 요청 2", longBody, "adaptive-long-2"),
        Mail("긴 요청 3", longBody, "adaptive-long-3"),
        Mail("짧은 요청", "확인 부탁드립니다.", "adaptive-long-4")
    };
    var longAnalyzer = new RecordingBatchAnalyzer(12);
    var longScanner = new MailActionScanner(
        new SequenceEmailSource(mixedMessages),
        new FollowUpPipeline(longAnalyzer, new FakeStore()));

    await longScanner.ScanAsync(new MailScanRequest(0, IncludeBody: true, DateTimeOffset.Now.AddDays(-30)));

    Assert(longAnalyzer.BatchSizes.SequenceEqual(new[] { 2, 2 }), "Long messages should reduce batch size without truncating input.");
}

static async Task MailScanRunsPreparedLlmBatchesConcurrently()
{
    var messages = Enumerable.Range(0, 36)
        .Select(index => Mail($"동시 요청 {index}", "확인 부탁드립니다.", $"concurrent-{index}"))
        .ToArray();
    var analyzer = new ConcurrentRecordingBatchAnalyzer(preferredBatchSize: 6);
    var store = new FakeStore { MutationDelay = TimeSpan.FromMilliseconds(5) };
    var scanner = new MailActionScanner(
        new SequenceEmailSource(messages),
        new FollowUpPipeline(analyzer, store));

    var summary = await scanner.ScanAsync(new MailScanRequest(
        MaxItems: 0,
        IncludeBody: true,
        Since: DateTimeOffset.Now.AddDays(-30),
        LlmInitialConcurrency: 2,
        LlmMaxConcurrency: 4));

    Assert(summary.ReadCount == 36, "Expected all messages read.");
    Assert(summary.IgnoredCount == 36, "Expected all concurrent analyzer results counted.");
    Assert(analyzer.MaxActiveBatchCalls == 2, "Expected fixed v0.5.0 effective concurrency of two.");
    Assert(store.MaxActiveMutations <= 1, "Persistence must stay serialized.");
}

static async Task MailScanPreservesDuplicateSourcesAcrossConcurrentBatches()
{
    var messages = new[]
    {
        Mail("중복 원본", "확인 부탁드립니다.", "duplicate-source"),
        Mail("다른 원본 1", "확인 부탁드립니다.", "unique-source-1"),
        Mail("중복 원본 재등장", "확인 부탁드립니다.", "duplicate-source"),
        Mail("다른 원본 2", "확인 부탁드립니다.", "unique-source-2")
    };
    var analyzer = new RecordingBatchAnalyzer(preferredBatchSize: 2);
    var store = new FakeStore();
    var scanner = new MailActionScanner(
        new SequenceEmailSource(messages),
        new FollowUpPipeline(analyzer, store));

    var summary = await scanner.ScanAsync(new MailScanRequest(
        MaxItems: 0,
        IncludeBody: true,
        Since: DateTimeOffset.Now.AddDays(-30),
        LlmInitialConcurrency: 2,
        LlmMaxConcurrency: 4));

    Assert(summary.ReadCount == 4, "Expected all messages read.");
    Assert(summary.DuplicateCount == 1, "Expected repeated source to become duplicate.");
    Assert(summary.IgnoredCount == 3, "Expected only unique sources to be analyzed and persisted as ignored.");
    Assert(store.Processed.Count == 3, "Expected only unique source hashes marked processed.");
}

static async Task MailScanCancellationStopsConcurrentScheduling()
{
    var messages = Enumerable.Range(0, 36)
        .Select(index => Mail($"취소 요청 {index}", "확인 부탁드립니다.", $"cancel-concurrent-{index}"))
        .ToArray();
    var analyzer = new CancellableBatchAnalyzer(preferredBatchSize: 6);
    var scanner = new MailActionScanner(
        new SequenceEmailSource(messages),
        new FollowUpPipeline(analyzer, new FakeStore()));
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    var scan = scanner.ScanAsync(
        new MailScanRequest(
            MaxItems: 0,
            IncludeBody: true,
            Since: DateTimeOffset.Now.AddDays(-30),
            LlmInitialConcurrency: 2,
            LlmMaxConcurrency: 4),
        cts.Token);
    await analyzer.WaitForStartedAsync(expectedStarted: 2, cts.Token);
    await cts.CancelAsync();

    try
    {
        await scan;
        Assert(false, "Expected cancellation to propagate.");
    }
    catch (OperationCanceledException)
    {
        Assert(analyzer.StartedBatchCalls == 2, "Pending batches should not start after cancellation.");
    }
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

static async Task SqliteGuardedTaskSaveIsAtomic()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var sourceA = StableHash.Create("guarded-source-a");
        var sourceB = StableHash.Create("guarded-source-b");
        var actionSignature = StableHash.Create("guarded-action");
        var taskA = new LocalTaskItem(
            Guid.NewGuid(),
            "원본 A 업무",
            null,
            sourceA,
            "guarded-source-a",
            0.91,
            "원자 저장",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        var duplicateSourceTask = taskA with { Id = Guid.NewGuid(), Title = "원본 A 중복" };
        var duplicateActionTask = taskA with { Id = Guid.NewGuid(), Title = "동일 업무 중복", SourceIdHash = sourceB, SourceId = "guarded-source-b" };

        var first = await store.TrySaveTaskWithProcessedSourcesAsync(taskA, actionSignature);
        var duplicateSource = await store.TrySaveTaskWithProcessedSourcesAsync(duplicateSourceTask, actionSignature: null);
        var duplicateAction = await store.TrySaveTaskWithProcessedSourcesAsync(duplicateActionTask, actionSignature);
        var openTasks = await store.ListOpenTasksAsync();

        Assert(first, "Expected first guarded save to create a task.");
        Assert(!duplicateSource, "Expected duplicate source guarded save to be rejected.");
        Assert(!duplicateAction, "Expected duplicate action-signature guarded save to be rejected.");
        Assert(openTasks.Count == 1, "Expected guarded duplicate rejection to keep one task.");
        Assert(await store.HasProcessedSourceAsync(sourceA), "Expected first source reserved.");
        Assert(await store.HasProcessedSourceAsync(sourceB), "Expected duplicate-action source still marked processed.");
        Assert(await store.HasProcessedSourceAsync(actionSignature), "Expected action signature reserved.");
    }
    finally
    {
        cleanup();
    }
}

static async Task SqliteGuardedReviewCandidateSaveIsAtomic()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var mailA = Mail("검토 요청", "금요일까지 자료 확인 부탁드립니다.", "guarded-review-source-a", "guarded-review-conversation");
        var mailB = Mail("RE: 검토 요청", "금요일까지 자료 확인 부탁드립니다.", "guarded-review-source-b", "guarded-review-conversation");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.64,
            "자료 확인",
            "자동 등록 전 검토가 필요합니다.",
            "확인 부탁",
            null);
        var actionSignature = FollowUpActionSignature.Create(mailA, analysis)
            ?? throw new InvalidOperationException("Expected actionable review to have an action signature.");
        var candidateA = ReviewCandidate.FromAnalysis(mailA, analysis, now);
        var duplicateSourceCandidate = ReviewCandidate.FromAnalysis(mailA, analysis with { Reason = "동일 원본" }, now.AddMinutes(1));
        var duplicateActionCandidate = ReviewCandidate.FromAnalysis(mailB, analysis, now.AddMinutes(2));

        var first = await store.TrySaveReviewCandidateWithProcessedSourcesAsync(candidateA, actionSignature);
        var duplicateSource = await store.TrySaveReviewCandidateWithProcessedSourcesAsync(duplicateSourceCandidate, actionSignature: null);
        var duplicateAction = await store.TrySaveReviewCandidateWithProcessedSourcesAsync(duplicateActionCandidate, actionSignature);
        var candidates = await store.ListReviewCandidatesAsync();

        Assert(first, "Expected first guarded review candidate save to create a candidate.");
        Assert(!duplicateSource, "Expected duplicate review source guarded save to be rejected.");
        Assert(!duplicateAction, "Expected duplicate review action-signature guarded save to be rejected.");
        Assert(candidates.Count == 1, "Expected guarded duplicate rejection to keep one review candidate.");
        Assert(await store.HasProcessedSourceAsync(mailA.SourceHash), "Expected first review source reserved.");
        Assert(await store.HasProcessedSourceAsync(mailB.SourceHash), "Expected duplicate-action review source still marked processed.");
        Assert(await store.HasProcessedSourceAsync(actionSignature), "Expected review action signature reserved.");
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

static async Task SqliteReviewFinalActionsMarkSourceProcessed()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var approveMail = Mail("승인 요청", "내일까지 승인 부탁드립니다.", "review-final-approve");
        var ignoreMail = Mail("참고 요청", "가능하면 확인해주세요.", "review-final-ignore");
        var analysis = new FollowUpAnalysis(
            FollowUpKind.ActionRequested,
            AnalysisDisposition.Review,
            0.72,
            "승인 요청 처리",
            "검토 후보",
            "승인 부탁",
            null);
        var approveCandidate = ReviewCandidate.FromAnalysis(approveMail, analysis, DateTimeOffset.UtcNow);
        var ignoreCandidate = ReviewCandidate.FromAnalysis(ignoreMail, analysis, DateTimeOffset.UtcNow);

        await store.SaveReviewCandidateAsync(approveCandidate);
        await store.SaveReviewCandidateAsync(ignoreCandidate);

        var task = await store.ResolveReviewCandidateAsTaskAsync(approveCandidate.Id, DateTimeOffset.UtcNow);
        var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(ignoreCandidate.Id, DateTimeOffset.UtcNow);

        Assert(task is not null, "Expected approve to create a task.");
        Assert(ignored, "Expected ignore to resolve candidate.");
        Assert(await store.HasProcessedSourceAsync(approveMail.SourceHash), "Approved review source should be marked processed.");
        Assert(await store.HasProcessedSourceAsync(ignoreMail.SourceHash), "Ignored review source should be marked processed.");
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

static async Task SqliteTaskFinalActionsMarkSourceProcessed()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var archive = BuildTask("archive-final-source", "보관 처리", now);
        var complete = BuildTask("complete-final-source", "완료 처리", now) with { Id = Guid.NewGuid() };
        var dismiss = BuildTask("dismiss-final-source", "삭제 처리", now) with { Id = Guid.NewGuid() };
        var manual = new LocalTaskItem(
            Guid.NewGuid(),
            "수동 업무 보관",
            null,
            null,
            null,
            1,
            "수동 입력",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        await store.SaveTaskAsync(archive);
        await store.SaveTaskAsync(complete);
        await store.SaveTaskAsync(dismiss);
        await store.SaveTaskAsync(manual);

        Assert(await store.ArchiveTaskAsync(archive.Id, now.AddMinutes(1)), "Expected archive to succeed.");
        Assert(await store.CompleteTaskAsync(complete.Id, now.AddMinutes(1)), "Expected complete to succeed.");
        Assert(await store.DismissTaskAsync(dismiss.Id, now.AddMinutes(1)), "Expected dismiss to succeed.");
        Assert(await store.ArchiveTaskAsync(manual.Id, now.AddMinutes(1)), "Expected manual task archive without source hash to succeed.");

        Assert(await store.HasProcessedSourceAsync(archive.SourceIdHash!), "Archived source should be marked processed.");
        Assert(await store.HasProcessedSourceAsync(complete.SourceIdHash!), "Completed source should be marked processed.");
        Assert(await store.HasProcessedSourceAsync(dismiss.SourceIdHash!), "Dismissed source should be marked processed.");
    }
    finally
    {
        cleanup();
    }

    static LocalTaskItem BuildTask(string sourceId, string title, DateTimeOffset now) => new(
        Guid.NewGuid(),
        title,
        null,
        StableHash.Create(sourceId),
        sourceId,
        0.9,
        "테스트",
        null,
        LocalTaskStatus.Open,
        null,
        now,
        now);
}

static async Task SqliteArchivedTasksCanBeListedAndRestored()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var task = new LocalTaskItem(
            Guid.NewGuid(),
            "복원 테스트",
            now.AddDays(1),
            StableHash.Create("restore-source"),
            "restore-source",
            0.9,
            "테스트",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now);
        await store.SaveTaskAsync(task);

        var archived = await store.ArchiveTaskAsync(task.Id, now.AddMinutes(1));
        var archiveList = await store.ListArchivedTasksAsync();
        var restored = await store.RestoreArchivedTaskAsync(task.Id, now.AddMinutes(2));
        var open = await store.ListOpenTasksAsync();
        var archiveAfterRestore = await store.ListArchivedTasksAsync();

        Assert(archived, "Expected archive to succeed.");
        Assert(archiveList.Count == 1 && archiveList[0].Id == task.Id, "Expected archived task in archive list.");
        Assert(restored, "Expected archived task restore to succeed.");
        Assert(open.Count == 1 && open[0].Id == task.Id, "Expected restored task in open list.");
        Assert(open[0].Status == LocalTaskStatus.Open, "Expected restored task status to be Open.");
        Assert(archiveAfterRestore.Count == 0, "Expected restored task to leave archive list.");
    }
    finally
    {
        cleanup();
    }
}

static async Task PipelineRecordsMultiRecipientReplyProgress()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var sentAt = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
        var sent = new EmailSnapshot(
            "sent-request",
            sentAt,
            "Me",
            "정산 확인 요청",
            "비용 정산 범위 확인 부탁드립니다.",
            "conversation-1",
            "Me",
            new[] { "Finance Team", "Design Partner", "QA User" },
            MailboxRecipientRole.Other);
        var replyOne = new EmailSnapshot(
            "reply-finance",
            sentAt.AddHours(1),
            "Finance Team",
            "RE: 정산 확인 요청",
            "확인했습니다.",
            "conversation-1",
            "Me",
            null,
            MailboxRecipientRole.Direct);
        var replyTwo = new EmailSnapshot(
            "reply-qa",
            sentAt.AddHours(2),
            "QA User",
            "RE: 정산 확인 요청",
            "QA 기준 확인했습니다.",
            "conversation-1",
            "Me",
            null,
            MailboxRecipientRole.Direct);

        var analyzer = new SequenceAnalyzer(
            new FollowUpAnalysis(
                FollowUpKind.WaitingForReply,
                AnalysisDisposition.AutoCreateTask,
                0.88,
                "정산 확인 요청",
                "다자 회신 대기",
                "확인 부탁드립니다",
                sentAt.AddDays(2)),
            FollowUpAnalysis.Ignore("reply"),
            FollowUpAnalysis.Ignore("reply"));
        var pipeline = new FollowUpPipeline(analyzer, store);

        await pipeline.ProcessAsync(sent);
        await pipeline.ProcessAsync(replyOne);
        await pipeline.ProcessAsync(replyTwo);

        var progress = (await store.ListReplyProgressAsync()).Single();
        Assert(progress.ExpectedCount == 3, "Expected three reply participants.");
        Assert(progress.ReceivedCount == 2, "Expected two received replies.");
        Assert(progress.SummaryText == "2/3명 회신", "Expected compact Korean summary.");
        Assert(progress.Participants.Any(participant => participant.DisplayName == "Design Partner" && !participant.HasReplied), "Expected missing participant to remain visible.");
    }
    finally
    {
        cleanup();
    }
}

static async Task PipelineSuggestsWaitingClosureFromReply()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var sentAt = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
        var sent = new EmailSnapshot(
            "closure-sent-request",
            sentAt,
            "Me",
            "견적 확인 요청",
            "20일까지 견적서 공유 부탁드립니다.",
            "closure-conversation-1",
            "Me",
            new[] { "Finance Team" },
            MailboxRecipientRole.Other);
        var reply = sent with
        {
            SourceId = "closure-reply-finance",
            ReceivedAt = sentAt.AddHours(2),
            SenderDisplay = "Finance Team",
            Body = "견적서 첨부드립니다.",
            RecipientDisplayNames = null,
            MailboxRecipientRole = MailboxRecipientRole.Direct
        };
        var analyzer = new SequenceAnalyzer(
            new FollowUpAnalysis(FollowUpKind.WaitingForReply, AnalysisDisposition.AutoCreateTask, 0.9, "견적 확인 요청", "회신 대기", "공유 부탁드립니다", sentAt.AddDays(5)),
            FollowUpAnalysis.Ignore("reply"));
        var pipeline = new FollowUpPipeline(analyzer, store);

        await pipeline.ProcessAsync(sent);
        await pipeline.ProcessAsync(reply);

        var suggestions = await store.ListWaitingClosureSuggestionsAsync();
        Assert(suggestions.Count == 1, "Expected one waiting closure suggestion.");
        Assert(suggestions[0].TriggerKind == WaitingClosureTriggerKind.RecipientReply, "Expected recipient reply trigger.");
        Assert(suggestions[0].DecisionSource == WaitingClosureDecisionSource.Rule, "Expected rule fallback suggestion.");
    }
    finally
    {
        cleanup();
    }
}

static async Task PipelineSuggestsWaitingClosureFromUserAcknowledgement()
{
    var store = new FakeStore();
    var now = new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.FromHours(9));
    var task = new LocalTaskItem(
        Guid.NewGuid(),
        "자료 공유 요청",
        null,
        StableHash.Create("ack-source"),
        "ack-source",
        0.8,
        "대기",
        null,
        LocalTaskStatus.Open,
        null,
        now.AddDays(-1),
        now.AddDays(-1),
        Kind: FollowUpKind.WaitingForReply,
        SourceConversationId: "ack-conversation",
        SourceRecipientDisplayNames: new[] { "Partner" });
    store.Tasks.Add(task);
    var ack = new EmailSnapshot(
        "ack-mail",
        now,
        "Me",
        "RE: 자료 공유 요청",
        "확인했습니다. 감사합니다.",
        "ack-conversation",
        "Me",
        new[] { "Partner" },
        MailboxRecipientRole.Other);

    var service = new WaitingClosureSuggestionService(store, new RuleBasedWaitingClosureJudge());
    var created = await service.CreateSuggestionsAsync(new[] { ack });
    var suggestion = (await store.ListWaitingClosureSuggestionsAsync()).Single();

    Assert(created == 1, "Expected acknowledgement to create one suggestion.");
    Assert(suggestion.TriggerKind == WaitingClosureTriggerKind.UserAcknowledgement, "Expected user acknowledgement trigger.");
}

static async Task LlmClosureJudgeCanRejectWeakReply()
{
    var now = DateTimeOffset.UtcNow;
    var task = new LocalTaskItem(Guid.NewGuid(), "견적 요청", null, "source", "source", 0.8, "대기", null, LocalTaskStatus.Open, null, now.AddDays(-1), now.AddDays(-1), Kind: FollowUpKind.WaitingForReply, SourceRecipientDisplayNames: new[] { "Partner" });
    var email = Mail("RE: 견적 요청", "확인해보겠습니다.", id: "weak-reply", conversationId: "conv", mailboxOwner: "Me", sender: "Partner");
    var trigger = new WaitingClosureTrigger(task, email, WaitingClosureTriggerKind.RecipientReply, 0.72, "요청한 상대의 회신이 감지되었습니다.");
    var judge = new LlmBackedWaitingClosureJudge(new FakeLlmClient("""{"shouldSuggest":false,"confidence":0.2,"reason":"아직 확인 예정이라 완료가 아닙니다"}"""));

    var judgment = await judge.JudgeAsync(trigger);

    Assert(!judgment.ShouldSuggest, "Expected LLM to reject weak reply closure suggestion.");
    Assert(judgment.Source == WaitingClosureDecisionSource.Llm, "Expected LLM decision source.");
}

static async Task WaitingClosureKeepAndArchiveDecisionsPersist()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var task = new LocalTaskItem(Guid.NewGuid(), "자료 요청", null, StableHash.Create("waiting-source"), "waiting-source", 0.8, "대기", null, LocalTaskStatus.Open, null, now.AddDays(-2), now, Kind: FollowUpKind.WaitingForReply, SourceConversationId: "closure-conversation-2", SourceRecipientDisplayNames: new[] { "Partner" });
        await store.SaveTaskAsync(task);
        var keep = new WaitingClosureSuggestion(Guid.NewGuid(), task.Id, task.Title, StableHash.Create("reply-keep"), WaitingClosureTriggerKind.RecipientReply, WaitingClosureDecisionSource.Rule, 0.7, "회신 감지", now, now);
        var archive = keep with { Id = Guid.NewGuid(), TriggerSourceHash = StableHash.Create("reply-archive") };
        await store.SaveWaitingClosureSuggestionAsync(keep);
        await store.SaveWaitingClosureSuggestionAsync(archive);

        var kept = await store.ResolveWaitingClosureSuggestionAsync(keep.Id, WaitingClosureResolution.Kept, now.AddMinutes(1));
        var stillOpen = await store.ListOpenTasksAsync();
        var archived = await store.ResolveWaitingClosureSuggestionAsync(archive.Id, WaitingClosureResolution.Archived, now.AddMinutes(2));
        var openAfterArchive = await store.ListOpenTasksAsync();
        var archivedTasks = await store.ListArchivedTasksAsync();

        Assert(kept, "Expected keep decision to resolve suggestion.");
        Assert(stillOpen.Any(item => item.Id == task.Id), "Keep must leave task open.");
        Assert(archived, "Expected archive decision to resolve suggestion.");
        Assert(!openAfterArchive.Any(item => item.Id == task.Id), "Archive must remove task from open list.");
        Assert(archivedTasks.Any(item => item.Id == task.Id), "Archive decision must move task to archive list.");
    }
    finally
    {
        cleanup();
    }
}

static Task WeeklyReviewSummarizesWaitingDebt()
{
    var now = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
    var aged = new LocalTaskItem(Guid.NewGuid(), "오래 기다림", null, "a", "a", 0.8, "대기", null, LocalTaskStatus.Open, null, now.AddDays(-8), now.AddDays(-8), Kind: FollowUpKind.WaitingForReply);
    var fresh = aged with { Id = Guid.NewGuid(), Title = "새 업무", Kind = FollowUpKind.ActionRequested, CreatedAt = now.AddDays(-1) };
    var archived = fresh with { Id = Guid.NewGuid(), Status = LocalTaskStatus.Archived, UpdatedAt = now.AddDays(-2) };
    var suggestion = new WaitingClosureSuggestion(Guid.NewGuid(), aged.Id, aged.Title, "reply", WaitingClosureTriggerKind.RecipientReply, WaitingClosureDecisionSource.Rule, 0.7, "회신 감지", now.AddHours(-1), now);
    var candidate = ReviewCandidate.FromAnalysis(Mail("확인", "검토 필요"), new FollowUpAnalysis(FollowUpKind.ReviewNeeded, AnalysisDisposition.Review, 0.5, "확인", "검토", null, null), now);

    var summary = new WeeklyReviewPlanner().Build(new[] { aged, fresh }, new[] { archived }, new[] { candidate }, new[] { suggestion }, now);

    Assert(summary.NewTaskCount == 1, "Expected fresh open task count.");
    Assert(summary.ArchivedTaskCount == 1, "Expected weekly archived count.");
    Assert(summary.AgedWaitingItems.Single().Id == aged.Id, "Expected aged waiting item.");
    Assert(summary.ClosureSuggestions.Single().Id == suggestion.Id, "Expected closure suggestion in weekly review.");
    return Task.CompletedTask;
}

static async Task MailWhereExportOmitsSourceIdsAndIncludesReplyProgress()
{
    var (store, _, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var openTask = new LocalTaskItem(
            Guid.NewGuid(),
            "열린 업무",
            now.AddDays(1),
            StableHash.Create("export-open-source"),
            "export-open-source",
            0.9,
            "원문 기반 사유",
            "원문 기반 증거",
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: "Finance Team");
        var archiveTask = openTask with
        {
            Id = Guid.NewGuid(),
            Title = "보관 업무",
            SourceIdHash = StableHash.Create("export-archived-source"),
            SourceId = "export-archived-source"
        };
        var waitingTask = openTask with
        {
            Id = Guid.NewGuid(),
            Title = "다자 요청",
            SourceIdHash = StableHash.Create("export-waiting-source"),
            SourceId = "export-waiting-source",
            Kind = FollowUpKind.WaitingForReply,
            SourceConversationId = "export-conversation",
            SourceRecipientDisplayNames = new[] { "Finance Team", "Design Partner" }
        };
        var reviewMail = Mail("검토 요청", "확인 부탁드립니다.", "export-review-source");
        var review = ReviewCandidate.FromAnalysis(
            reviewMail,
            new FollowUpAnalysis(FollowUpKind.ReviewNeeded, AnalysisDisposition.Review, 0.55, "검토 항목", "원문 기반 후보", "원문 증거", null),
            now);

        await store.SaveTaskAsync(openTask);
        await store.SaveTaskAsync(archiveTask);
        await store.SaveTaskAsync(waitingTask);
        await store.ArchiveTaskAsync(archiveTask.Id, now.AddMinutes(1));
        await store.SaveReviewCandidateAsync(review);
        await store.RecordReplyObservationAsync(new EmailSnapshot("reply-export", now.AddMinutes(5), "Finance Team", "RE", null, "export-conversation"));

        var snapshot = await new MailWhereExportService(store).BuildSnapshotAsync(now);
        var json = MailWhereExportService.ToJson(snapshot);

        Assert(snapshot.OpenTasks.Count == 2, "Expected open export tasks.");
        Assert(snapshot.ArchivedTasks.Count == 1, "Expected archived export task.");
        Assert(snapshot.ReviewItems.Count == 1, "Expected review export item.");
        Assert(snapshot.ReplyProgress.Single().SummaryText == "1/2명 회신", "Expected reply progress in export.");
        Assert(json.Contains("열린 업무", StringComparison.Ordinal), "Expected safe task title in export.");
        Assert(!json.Contains("export-open-source", StringComparison.Ordinal), "Expected source ids omitted from export.");
        Assert(!json.Contains(StableHash.Create("export-open-source"), StringComparison.Ordinal), "Expected source hashes omitted from export.");
        Assert(!json.Contains("원문 기반 증거", StringComparison.Ordinal), "Expected evidence snippets omitted from export.");
        Assert(!json.Contains("sourceId", StringComparison.OrdinalIgnoreCase), "Expected no source id field in export JSON.");
    }
    finally
    {
        cleanup();
    }
}

static async Task MailWhereCliManifestAndHealthEmitProviderEnvelopes()
{
    var manifest = await RunCliAsync("manifest", "--json");
    var health = await RunCliAsync("health", "--json");

    Assert(manifest.ExitCode == CliApp.ExitSuccess, "Expected manifest to succeed.");
    Assert(health.ExitCode == CliApp.ExitSuccess, "Expected health to succeed.");

    using var manifestJson = JsonDocument.Parse(manifest.Stdout);
    using var healthJson = JsonDocument.Parse(health.Stdout);
    AssertProviderEnvelope(manifestJson.RootElement, ok: true);
    AssertProviderEnvelope(healthJson.RootElement, ok: true);

    var manifestData = manifestJson.RootElement.GetProperty("data");
    Assert(manifestData.GetProperty("read_only").GetBoolean(), "Expected manifest to declare read-only mode.");
    Assert(manifestData.GetProperty("no_outlook_com").GetBoolean(), "Expected manifest to declare no Outlook COM dependency.");
    Assert(manifestData.GetProperty("exit_codes").GetProperty("expected_unavailable").GetInt32() == CliApp.ExitExpectedUnavailable, "Expected unavailable exit code in manifest.");
    Assert(manifestData.GetProperty("commands").EnumerateArray().Any(command => command.GetProperty("name").GetString() == "export"), "Expected export command in manifest.");
    Assert(manifestData.GetProperty("commands").EnumerateArray().Any(command => command.GetProperty("name").GetString() == "search-mail"), "Expected search-mail command in manifest.");

    var healthData = healthJson.RootElement.GetProperty("data");
    Assert(healthData.GetProperty("read_only").GetBoolean(), "Expected health to declare read-only mode.");
    Assert(healthData.GetProperty("commands").EnumerateArray().Any(command => command.GetString() == "list-tasks"), "Expected list-tasks in health command list.");
    Assert(healthData.GetProperty("commands").EnumerateArray().Any(command => command.GetString() == "search-mail"), "Expected search-mail in health command list.");
}

static async Task MailWhereCliMissingDatabaseReturnsJsonErrorWithoutCreatingFiles()
{
    var directory = Path.Combine(Path.GetTempPath(), "MailWhere.Tests", Guid.NewGuid().ToString("N"));
    var dbPath = Path.Combine(directory, "followups.sqlite");
    try
    {
        var result = await RunCliAsync("export", "--json", "--db", dbPath);

        Assert(result.ExitCode == CliApp.ExitExpectedUnavailable, "Expected missing database to use expected-unavailable exit code.");
        using var json = JsonDocument.Parse(result.Stdout);
        AssertProviderEnvelope(json.RootElement, ok: false);
        Assert(json.RootElement.GetProperty("code").GetString() == "database-not-found", "Expected database-not-found error code.");
        Assert(!File.Exists(dbPath), "Expected missing database file not to be created.");
        Assert(!File.Exists(dbPath + "-wal"), "Expected missing WAL file not to be created.");
        Assert(!File.Exists(dbPath + "-shm"), "Expected missing SHM file not to be created.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task MailWhereCliReadCommandsEmitSanitizedSchemas()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    try
    {
        var now = DateTimeOffset.UtcNow;
        var openSource = "cli-open-source-secret";
        var archivedSource = "cli-archived-source-secret";
        var reviewSource = "cli-review-source-secret";
        var privateRecipient = "Private Recipient <private-recipient@example.com>";
        var sensitiveEvidence = "cli-secret-evidence";
        var sensitiveReason = "cli-secret-reason";
        var openTask = new LocalTaskItem(
            Guid.NewGuid(),
            "CLI 열린 업무",
            now.AddDays(1),
            StableHash.Create(openSource),
            openSource,
            0.9,
            sensitiveReason,
            sensitiveEvidence,
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: "Safe Sender",
            SourceConversationId: "cli-private-conversation",
            SourceRecipientDisplayNames: new[] { privateRecipient });
        var archivedTask = openTask with
        {
            Id = Guid.NewGuid(),
            Title = "CLI 보관 업무",
            SourceIdHash = StableHash.Create(archivedSource),
            SourceId = archivedSource
        };
        var review = ReviewCandidate.FromAnalysis(
            Mail("CLI 검토", "raw body must stay transient", reviewSource, sender: "Safe Reviewer", recipients: new[] { privateRecipient }),
            new FollowUpAnalysis(FollowUpKind.ReviewNeeded, AnalysisDisposition.Review, 0.55, "CLI 검토 후보", sensitiveReason, sensitiveEvidence, null),
            now);

        await store.SaveTaskAsync(openTask);
        await store.SaveTaskAsync(archivedTask);
        await store.ArchiveTaskAsync(archivedTask.Id, now.AddMinutes(1));
        await store.SaveReviewCandidateAsync(review);

        var export = await RunCliAsync("export", "--json", "--db", dbPath, "--archived-limit", "10");
        var tasks = await RunCliAsync("list-tasks", "--json", "--db", dbPath, "--status", "all", "--due-window", "all", "--limit", "10");
        var candidates = await RunCliAsync("list-review-candidates", "--json", "--db", dbPath, "--limit", "10");

        Assert(export.ExitCode == CliApp.ExitSuccess, "Expected export command to succeed.");
        Assert(tasks.ExitCode == CliApp.ExitSuccess, "Expected list-tasks command to succeed.");
        Assert(candidates.ExitCode == CliApp.ExitSuccess, "Expected list-review-candidates command to succeed.");

        using var exportJson = JsonDocument.Parse(export.Stdout);
        using var tasksJson = JsonDocument.Parse(tasks.Stdout);
        using var candidatesJson = JsonDocument.Parse(candidates.Stdout);
        AssertProviderEnvelope(exportJson.RootElement, ok: true);
        AssertProviderEnvelope(tasksJson.RootElement, ok: true);
        AssertProviderEnvelope(candidatesJson.RootElement, ok: true);

        Assert(exportJson.RootElement.GetProperty("data").GetProperty("open_tasks").GetArrayLength() == 1, "Expected one open task in CLI export.");
        Assert(exportJson.RootElement.GetProperty("data").GetProperty("archived_tasks").GetArrayLength() == 1, "Expected one archived task in CLI export.");
        Assert(exportJson.RootElement.GetProperty("data").GetProperty("review_items").GetArrayLength() == 1, "Expected one review item in CLI export.");
        Assert(tasksJson.RootElement.GetProperty("data").GetProperty("tasks").GetArrayLength() == 2, "Expected list-tasks all to include open and archived tasks.");
        Assert(candidatesJson.RootElement.GetProperty("data").GetProperty("candidates").GetArrayLength() == 1, "Expected one review candidate.");

        foreach (var output in new[] { export.Stdout, tasks.Stdout, candidates.Stdout })
        {
            Assert(!output.Contains(openSource, StringComparison.Ordinal), "Expected source id omitted from CLI JSON.");
            Assert(!output.Contains(archivedSource, StringComparison.Ordinal), "Expected archived source id omitted from CLI JSON.");
            Assert(!output.Contains(reviewSource, StringComparison.Ordinal), "Expected review source id omitted from CLI JSON.");
            Assert(!output.Contains(StableHash.Create(openSource), StringComparison.Ordinal), "Expected source hash omitted from CLI JSON.");
            Assert(!output.Contains(StableHash.Create(archivedSource), StringComparison.Ordinal), "Expected archived source hash omitted from CLI JSON.");
            Assert(!output.Contains(StableHash.Create(reviewSource), StringComparison.Ordinal), "Expected review source hash omitted from CLI JSON.");
            Assert(!output.Contains(sensitiveEvidence, StringComparison.Ordinal), "Expected evidence snippet omitted from CLI JSON.");
            Assert(!output.Contains(sensitiveReason, StringComparison.Ordinal), "Expected analysis reason omitted from CLI JSON.");
            Assert(!output.Contains(privateRecipient, StringComparison.Ordinal), "Expected full recipient list omitted from CLI JSON.");
            Assert(!output.Contains("private-recipient@example.com", StringComparison.OrdinalIgnoreCase), "Expected recipient email omitted from CLI JSON.");
            Assert(!output.Contains("raw body", StringComparison.OrdinalIgnoreCase), "Expected raw body omitted from CLI JSON.");
            Assert(!ContainsJsonPropertyName(output, "source_id"), "Expected no source_id JSON property.");
            Assert(!ContainsJsonPropertyName(output, "source_id_hash"), "Expected no source_id_hash JSON property.");
            Assert(!ContainsJsonPropertyName(output, "evidence_snippet"), "Expected no evidence_snippet JSON property.");
        }
    }
    finally
    {
        cleanup();
    }
}

static async Task MailWhereCliSearchMailIsSqliteOnlyAndSanitized()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    await using var mirror = new SqliteMailMirrorStore(dbPath);
    try
    {
        await mirror.InitializeAsync();
        await mirror.UpsertBatchAsync(new[]
        {
            MirrorMessage("secret-store", "secret-entry", "검색 제목", "needle body with bounded snippet", MailSourceFolder.Inbox, sender: "Safe Sender")
        });
        await store.SaveTaskAsync(new LocalTaskItem(
            Guid.NewGuid(),
            "Export task",
            null,
            StableHash.Create("source-secret"),
            "source-secret",
            0.9,
            "reason",
            "evidence",
            LocalTaskStatus.Open,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        var search = await RunCliAsync("search-mail", "--json", "--db", dbPath, "--query", "needle", "--folder", "inbox", "--limit", "5");
        var export = await RunCliAsync("export", "--json", "--db", dbPath);

        Assert(search.ExitCode == CliApp.ExitSuccess, "Expected search-mail command to succeed.");
        using var searchJson = JsonDocument.Parse(search.Stdout);
        AssertProviderEnvelope(searchJson.RootElement, ok: true);
        var results = searchJson.RootElement.GetProperty("data").GetProperty("results");
        Assert(results.GetArrayLength() == 1, "Expected one mail search hit.");
        Assert(results[0].GetProperty("can_open_source").GetBoolean(), "Expected opaque source-open capability flag.");
        Assert(results[0].GetProperty("snippet").GetString()!.Length <= 160, "Expected bounded snippet.");
        Assert(!search.Stdout.Contains("secret-store", StringComparison.Ordinal), "Expected StoreID omitted from CLI search JSON.");
        Assert(!search.Stdout.Contains("secret-entry", StringComparison.Ordinal), "Expected EntryID omitted from CLI search JSON.");
        Assert(!ContainsJsonPropertyName(search.Stdout, "store_id"), "Expected no store_id JSON property.");
        Assert(!ContainsJsonPropertyName(search.Stdout, "entry_id"), "Expected no entry_id JSON property.");
        Assert(!export.Stdout.Contains("needle body", StringComparison.Ordinal), "Expected default export remain body-free.");
    }
    finally
    {
        cleanup();
    }
}

static Task MailWhereCliProjectReferencesOnlyCoreAndStorage()
{
    var repoRoot = FindRepoRoot();
    var projectPath = Path.Combine(repoRoot, "src", "MailWhere.Cli", "MailWhere.Cli.csproj");
    Assert(File.Exists(projectPath), "Expected CLI project file to exist.");

    var document = XDocument.Load(projectPath);
    var projectReferences = document
        .Descendants("ProjectReference")
        .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    Assert(projectReferences.SequenceEqual(new[]
    {
        "../MailWhere.Core/MailWhere.Core.csproj",
        "../MailWhere.Storage/MailWhere.Storage.csproj"
    }, StringComparer.Ordinal), $"Expected CLI project to reference only Core and Storage, found: {string.Join(", ", projectReferences)}.");

    var projectText = File.ReadAllText(projectPath);
    Assert(!projectText.Contains("MailWhere.Windows", StringComparison.Ordinal), "CLI must not reference MailWhere.Windows.");
    Assert(!projectText.Contains("MailWhere.OutlookCom", StringComparison.Ordinal), "CLI must not reference MailWhere.OutlookCom.");
    Assert(!projectText.Contains("<UseWPF>", StringComparison.OrdinalIgnoreCase), "CLI must not enable WPF.");
    Assert(!projectText.Contains("<UseWindowsForms>", StringComparison.OrdinalIgnoreCase), "CLI must not enable Windows Forms.");
    Assert(!projectText.Contains("<EnableWindowsTargeting>", StringComparison.OrdinalIgnoreCase), "CLI must not require Windows targeting.");
    Assert(projectText.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal), "CLI should target cross-platform net10.0.");
    return Task.CompletedTask;
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


static async Task MailMirrorFtsInsertUpdateDeleteRebuild()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var first = MirrorMessage("store", "entry-1", "분기 보고", "alpha body", MailSourceFolder.Inbox, conversationId: "thread-1");
        await mirror.UpsertBatchAsync(new[] { first });

        var subjectHits = await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "분기", Limit: 10));
        Assert(subjectHits.Count == 1 && subjectHits[0].Locator.EntryId == "entry-1", "Expected inserted subject searchable.");

        var updated = first with { Subject = "다른 제목", BodyText = "새로운 beta 본문" };
        await mirror.UpsertBatchAsync(new[] { updated });
        var oldHits = await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "alpha", Limit: 10));
        var newHits = await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "beta", Limit: 10));
        Assert(oldHits.Count == 0, "Expected old FTS terms removed on update.");
        Assert(newHits.Count == 1, "Expected new FTS terms searchable on update.");

        await ClearMailMirrorFtsAsync(dbPath);
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "beta", Limit: 10))).Count == 0, "Expected cleared FTS to hide term before rebuild.");
        await mirror.RebuildFtsAsync();
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "beta", Limit: 10))).Count == 1, "Expected explicit rebuild restore FTS parity.");

        await mirror.DeleteAsync(new[] { first.Locator });
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "beta", Limit: 10))).Count == 0, "Expected delete remove FTS terms.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorBatchCheckpointAtomic()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var batch = Enumerable.Range(0, SqliteMailMirrorStore.MaxWriteBatchSize)
            .Select(i => MirrorMessage("store", $"entry-{i:00}", $"제목 {i}", $"atomic body {i}", MailSourceFolder.Inbox))
            .ToArray();
        await mirror.UpsertBatchAsync(batch, new MailMirrorCheckpoint("Inbox", "checkpoint-1"));

        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages") == SqliteMailMirrorStore.MaxWriteBatchSize, "Expected max-sized batch rows committed.");
        var checkpoint = await QuerySingleStringAsync(dbPath, "SELECT checkpoint FROM mail_mirror_checkpoints WHERE folder = 'Inbox'");
        Assert(checkpoint == "checkpoint-1", "Expected checkpoint committed with batch.");

        try
        {
            var tooLarge = Enumerable.Range(0, SqliteMailMirrorStore.MaxWriteBatchSize + 1)
                .Select(i => MirrorMessage("store", $"too-large-{i:00}", "too large", "too large", MailSourceFolder.Inbox))
                .ToArray();
            await mirror.UpsertBatchAsync(tooLarge, new MailMirrorCheckpoint("Inbox", "bad-checkpoint"));
            Assert(false, "Expected oversized direct write batch rejected.");
        }
        catch (ArgumentOutOfRangeException)
        {
            // Expected: callers must supply already bounded batches.
        }

        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages WHERE entry_id LIKE 'too-large-%'") == 0, "Expected rejected oversized batch not written.");
        Assert(await mirror.GetCheckpointAsync("Inbox") == "checkpoint-1", "Expected rejected oversized batch not advance checkpoint.");

        try
        {
            await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "entry-fail", "fail", "fail", MailSourceFolder.Inbox) }, new MailMirrorCheckpoint(null!, "bad"));
            Assert(false, "Expected invalid checkpoint fail before commit.");
        }
        catch
        {
            // Expected: storage-scoped transaction should roll back inserted row with bad checkpoint.
        }

        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages WHERE entry_id = 'entry-fail'") == 0, "Expected failed batch rolled back.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorSearchFiltersNormalizeAndShortQueryFallback()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[]
        {
            MirrorMessage("store", "inbox-1", "검색 대상", "한\0글\r\n본문", MailSourceFolder.Inbox, sender: "Alice", recipients: new[] { "Bob" }, conversationId: "thread-a"),
            MirrorMessage("store", "sent-1", "다른 메일", "영문 body", MailSourceFolder.Sent, sender: "Me", recipients: new[] { "Carol" }, conversationId: "thread-b")
        });

        var shortQueryHits = await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "한", Limit: 10));
        Assert(shortQueryHits.Count == 1 && shortQueryHits[0].Snippet.Contains("한글", StringComparison.Ordinal), "Expected short query bounded LIKE fallback over normalized body.");

        var filtered = await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "메일", Folder: MailSourceFolder.Sent, SenderOrRecipient: "Carol", ConversationId: "thread-b", Limit: 10));
        Assert(filtered.Count == 1 && filtered[0].Locator.EntryId == "sent-1", "Expected deterministic metadata filters.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorPreservesTaskBoardDatabase()
{
    var (store, dbPath, cleanup) = await CreateTempStoreAsync();
    await using var mirror = new SqliteMailMirrorStore(dbPath);
    try
    {
        var mail = Mail("기존 업무", "내일까지 회신 부탁드립니다.", id: "task-source");
        await store.SaveTaskAsync(LocalTaskItem.FromAnalysis(mail, await new RuleBasedFollowUpAnalyzer().AnalyzeAsync(mail), DateTimeOffset.UtcNow));
        await mirror.InitializeAsync();
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "entry", "mirror", "body", MailSourceFolder.Inbox) });

        var tasks = await store.ListOpenTasksAsync();
        Assert(tasks.Count == 1 && tasks[0].SourceId == "task-source", "Expected mirror schema preserve existing task-board behavior.");
    }
    finally
    {
        cleanup();
    }
}


static async Task MailMirrorReconcileDeletesUnseenAndFtsTerms()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "gone", "삭제 대상", "delete-token", MailSourceFolder.Inbox) });
        var source = new FakeInventorySource(Array.Empty<MailInventoryItem>());

        await new MailMirrorBackfillService(source, mirror).RunAuthoritativeReconcileAsync();

        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "delete-token", Folder: MailSourceFolder.Inbox))).Count == 0, "Expected unseen row and FTS terms deleted together.");
        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_mirror_generations WHERE folder = 'Inbox'") == 1, "Expected completed Inbox generation recorded.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorReconcileHandlesInboxToSentMove()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "old-inbox", "이동", "move-token", MailSourceFolder.Inbox) });
        var moved = InventoryItem("store", "new-sent", MailSourceFolder.Sent, 1, "이동", "move-token");
        var source = new FakeInventorySource(new[] { moved });

        await new MailMirrorBackfillService(source, mirror).RunAuthoritativeReconcileAsync();

        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "move-token", Folder: MailSourceFolder.Inbox))).Count == 0, "Expected old Inbox locator disappeared.");
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "이동", Folder: MailSourceFolder.Sent))).Count == 1, "Expected new Sent locator appeared.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorInterruptedReconcileRetainsUnseen()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "keep", "보존", "keep-token", MailSourceFolder.Inbox) });
        var source = new FakeInventorySource(Array.Empty<MailInventoryItem>());
        source.IncompleteFolders.Add(MailSourceFolder.Inbox);

        await new MailMirrorBackfillService(source, mirror).RunAuthoritativeReconcileAsync();

        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "keep-token", Folder: MailSourceFolder.Inbox))).Count == 1, "Expected incomplete generation retain unseen rows.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorWarningReconcileRetainsUnseen()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "keep-warning", "보존", "warning-token", MailSourceFolder.Inbox) });
        var source = new FakeInventorySource(Array.Empty<MailInventoryItem>());
        source.WarningFolders.Add(MailSourceFolder.Inbox);

        var summary = await new MailMirrorBackfillService(source, mirror).RunAuthoritativeReconcileAsync();

        Assert(summary.Warnings.Any(warning => warning.Code == "fake-inventory-warning"), "Expected inventory warning surfaced.");
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "warning-token", Folder: MailSourceFolder.Inbox))).Count == 1, "Expected warning generation retain unseen rows and FTS.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorEventHintOnlyWakesMissedEventRecovery()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var queue = new MailMirrorEventHintQueue();
        queue.NotifyNewMailHint();
        Assert(queue.ConsumePendingHint(), "Expected event hint wake one sync.");
        Assert(!queue.ConsumePendingHint(), "Expected hint consumed without durable side effect.");
        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages") == 0, "Expected event hint not write mail rows.");

        var missed = InventoryItem("store", "missed", MailSourceFolder.Inbox, 1, "missed", "missed-token");
        await new MailMirrorBackfillService(new FakeInventorySource(new[] { missed }), mirror).RunAuthoritativeReconcileAsync();
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "missed", Folder: MailSourceFolder.Inbox))).Count == 1, "Expected periodic reconcile recover missed event.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorBackfillHydratesOnlyNewChangedCheckpointsFolders()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var unchanged = InventoryItem("store", "inbox-old", MailSourceFolder.Inbox, 0, "old", "old body");
        await mirror.UpsertBatchAsync(new[] { MessageFrom(unchanged, "old body") });
        var changed = unchanged with { EntryId = "inbox-changed", LastModifiedAt = unchanged.LastModifiedAt.AddMinutes(1), Subject = "changed" };
        await mirror.UpsertBatchAsync(new[] { MessageFrom(changed with { LastModifiedAt = unchanged.LastModifiedAt }, "stale body") });
        var source = new FakeInventorySource(new[]
        {
            unchanged,
            changed,
            InventoryItem("store", "sent-new", MailSourceFolder.Sent, 2, "sent", "sent body")
        });
        var service = new MailMirrorBackfillService(source, mirror);

        var summary = await service.RunInitialBackfillAsync();

        Assert(summary.SeenCount == 3, "Expected Inbox and Sent all-history inventory.");
        Assert(summary.HydratedCount == 2, "Expected only new/changed bodies hydrated.");
        Assert(summary.SkippedUnchangedCount == 1, "Expected unchanged item skip hydration.");
        Assert(source.HydrateCalls.Count == 2 && !source.HydrateCalls.Contains(unchanged.Locator), "Expected unchanged locator not hydrated.");
        Assert((await mirror.SearchAsync(new MailMirrorSearchRequest(Query: "sent", Folder: MailSourceFolder.Sent))).Count == 1, "Expected sent message searchable.");
        Assert(await mirror.GetCheckpointAsync("Inbox") is not null, "Expected independent Inbox checkpoint.");
        Assert(await mirror.GetCheckpointAsync("Sent") is not null, "Expected independent Sent checkpoint.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorBackfillCancelResumeKeepsAtomicBatchesNoDuplicates()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var items = Enumerable.Range(0, 26)
            .Select(i => InventoryItem("store", $"inbox-{i:00}", MailSourceFolder.Inbox, i, $"subject {i}", $"body {i}"))
            .ToArray();
        var firstSource = new FakeInventorySource(items) { CancelHydrationAfter = 25 };
        var service = new MailMirrorBackfillService(firstSource, mirror);

        try
        {
            await service.RunInitialBackfillAsync();
            Assert(false, "Expected cancellation after first committed batch.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages") == 25, "Expected only committed 25-row batch before cancellation.");

        var resumedSource = new FakeInventorySource(items);
        await new MailMirrorBackfillService(resumedSource, mirror).RunInitialBackfillAsync();

        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages") == 26, "Expected resume complete without duplicate rows.");
        Assert(resumedSource.HydrateCalls.Count == 1 && resumedSource.HydrateCalls[0].EntryId == "inbox-25", "Expected resume hydrate only post-checkpoint item.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorBackfillIsolatesHydrationFailures()
{
    var (mirror, dbPath, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        var failed = InventoryItem("store", "bad", MailSourceFolder.Inbox, 1, "bad", "bad body");
        var good = InventoryItem("store", "good", MailSourceFolder.Inbox, 2, "good", "good body");
        var source = new FakeInventorySource(new[] { failed, good });
        source.FailHydrationFor.Add(failed.Locator);

        var summary = await new MailMirrorBackfillService(source, mirror).RunInitialBackfillAsync();

        Assert(summary.Warnings.Any(warning => warning.Code == "mail-hydration-failed" && warning.SanitizedErrorClass == nameof(InvalidOperationException)), "Expected sanitized per-item failure.");
        Assert(await QueryScalarIntAsync(dbPath, "SELECT COUNT(*) FROM mail_messages") == 1, "Expected later item still stored despite one failure.");
        Assert(await mirror.GetCheckpointAsync("Inbox") is null, "Expected checkpoint not advance past failed item.");
    }
    finally
    {
        await mirror.DisposeAsync();
        cleanup();
    }
}

static async Task MailMirrorConcurrentSearchesUseSerializedReader()
{
    var (mirror, _, cleanup) = await CreateTempMirrorStoreAsync();
    try
    {
        await mirror.UpsertBatchAsync(new[] { MirrorMessage("store", "entry", "동시 검색", "reader body", MailSourceFolder.Inbox) });
        var searches = Enumerable.Range(0, 8)
            .Select(_ => mirror.SearchAsync(new MailMirrorSearchRequest(Query: "reader", Limit: 5)))
            .ToArray();
        var results = await Task.WhenAll(searches);
        Assert(results.All(result => result.Count == 1), "Expected concurrent callers serialized over one reader without failures.");
    }
    finally
    {
        await mirror.DisposeAsync();
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


static async Task<(SqliteMailMirrorStore Store, string DbPath, Action Cleanup)> CreateTempMirrorStoreAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "MailWhere.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var dbPath = Path.Combine(directory, "test.db");
    var store = new SqliteMailMirrorStore(dbPath);
    await store.InitializeAsync();
    return (store, dbPath, () =>
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Test cleanup best-effort.
        }
    });
}


static MailInventoryItem InventoryItem(string storeId, string entryId, MailSourceFolder folder, int minutes, string subject, string body) =>
    new(
        storeId,
        entryId,
        folder,
        new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero).AddMinutes(minutes),
        folder == MailSourceFolder.Inbox ? new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes) : null,
        folder == MailSourceFolder.Sent ? new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes) : null,
        subject,
        "sender",
        "thread",
        new[] { "recipient" });

static MailMirrorMessage MessageFrom(MailInventoryItem item, string body) => new(
    item.StoreId,
    item.EntryId,
    item.Folder,
    item.LastModifiedAt,
    item.Subject,
    item.SenderDisplay,
    body,
    item.ReceivedAt,
    item.SentAt,
    item.ConversationId,
    item.RecipientDisplayNames);

static MailMirrorMessage MirrorMessage(
    string storeId,
    string entryId,
    string subject,
    string body,
    MailSourceFolder folder,
    string sender = "sender",
    IReadOnlyList<string>? recipients = null,
    string? conversationId = null) => new(
        storeId,
        entryId,
        folder,
        new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero),
        subject,
        sender,
        body,
        ReceivedAt: folder == MailSourceFolder.Inbox ? new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero) : null,
        SentAt: folder == MailSourceFolder.Sent ? new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero) : null,
        ConversationId: conversationId,
        RecipientDisplayNames: recipients);

static async Task ClearMailMirrorFtsAsync(string dbPath)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM mail_messages_fts";
    await command.ExecuteNonQueryAsync();
}

static async Task<int> QueryScalarIntAsync(string dbPath, string sql)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<string?> QuerySingleStringAsync(string dbPath, string sql)
{
    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(await command.ExecuteScalarAsync());
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

static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(params string[] args)
{
    await using var stdout = new StringWriter();
    await using var stderr = new StringWriter();
    var exitCode = await CliApp.RunAsync(args, stdout, stderr);
    return (exitCode, stdout.ToString(), stderr.ToString());
}

static void AssertProviderEnvelope(JsonElement root, bool ok)
{
    Assert(root.GetProperty("provider").GetString() == CliApp.ProviderName, "Expected MailWhere provider.");
    Assert(root.GetProperty("contract_version").GetString() == CliApp.ContractVersion, "Expected v1 contract.");
    Assert(!string.IsNullOrWhiteSpace(root.GetProperty("app_version").GetString()), "Expected app version.");
    Assert(DateTimeOffset.TryParse(root.GetProperty("generated_at").GetString(), out _), "Expected parseable generated_at.");
    Assert(root.GetProperty("ok").GetBoolean() == ok, $"Expected ok={ok}.");
    if (ok)
    {
        Assert(root.TryGetProperty("data", out var data) && data.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined, "Expected success data.");
        Assert(!root.TryGetProperty("code", out var code) || code.ValueKind == JsonValueKind.Null, "Expected no error code on success.");
    }
    else
    {
        Assert(root.TryGetProperty("code", out var code) && !string.IsNullOrWhiteSpace(code.GetString()), "Expected error code.");
        Assert(root.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString()), "Expected error message.");
    }
}

static bool ContainsJsonPropertyName(string json, string propertyName)
{
    using var document = JsonDocument.Parse(json);
    return ContainsJsonPropertyName(document.RootElement, propertyName);

    static bool ContainsJsonPropertyName(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                        || ContainsJsonPropertyName(property.Value, propertyName))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsJsonPropertyName(item, propertyName))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MailWhere.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MailWhere.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find repository root.");
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
    private int _activeMutations;
    private int _maxActiveMutations;

    public List<LocalTaskItem> Tasks { get; } = [];
    public List<ReviewCandidate> Candidates { get; } = [];
    public List<ReplyReceipt> ReplyReceipts { get; } = [];
    public List<WaitingClosureSuggestion> ClosureSuggestions { get; } = [];
    public HashSet<string> Processed { get; } = [];
    public Dictionary<string, string> AppState { get; } = new(StringComparer.Ordinal);
    public TimeSpan MutationDelay { get; init; }
    public int MaxActiveMutations => Volatile.Read(ref _maxActiveMutations);

    public Task<bool> HasProcessedSourceAsync(string sourceIdHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Processed.Contains(sourceIdHash));

    public async Task SaveTaskAsync(LocalTaskItem task, CancellationToken cancellationToken = default)
    {
        await TrackMutationAsync(() => Tasks.Add(task), cancellationToken);
    }

    public async Task SaveReviewCandidateAsync(ReviewCandidate candidate, CancellationToken cancellationToken = default)
    {
        await TrackMutationAsync(() => Candidates.Add(candidate), cancellationToken);
    }

    public async Task<bool> TrySaveTaskWithProcessedSourcesAsync(LocalTaskItem task, string? actionSignature, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await TrackMutationAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(task.SourceIdHash) || Processed.Contains(task.SourceIdHash))
            {
                saved = false;
                return;
            }

            Processed.Add(task.SourceIdHash);
            if (!string.IsNullOrWhiteSpace(actionSignature))
            {
                if (Processed.Contains(actionSignature))
                {
                    saved = false;
                    return;
                }

                Processed.Add(actionSignature);
            }

            Tasks.Add(task);
            saved = true;
        }, cancellationToken);
        return saved;
    }

    public async Task<bool> TrySaveReviewCandidateWithProcessedSourcesAsync(ReviewCandidate candidate, string? actionSignature, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await TrackMutationAsync(() =>
        {
            if (Processed.Contains(candidate.SourceIdHash))
            {
                saved = false;
                return;
            }

            Processed.Add(candidate.SourceIdHash);
            if (!string.IsNullOrWhiteSpace(actionSignature))
            {
                if (Processed.Contains(actionSignature))
                {
                    saved = false;
                    return;
                }

                Processed.Add(actionSignature);
            }

            Candidates.Add(candidate);
            saved = true;
        }, cancellationToken);
        return saved;
    }

    public async Task<bool> TryMarkProcessedSourcesAsync(string sourceIdHash, string? actionSignature, CancellationToken cancellationToken = default)
    {
        var marked = false;
        await TrackMutationAsync(() =>
        {
            if (Processed.Contains(sourceIdHash))
            {
                marked = false;
                return;
            }

            Processed.Add(sourceIdHash);
            if (!string.IsNullOrWhiteSpace(actionSignature))
            {
                if (Processed.Contains(actionSignature))
                {
                    marked = false;
                    return;
                }

                Processed.Add(actionSignature);
            }

            marked = true;
        }, cancellationToken);
        return marked;
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

    public async Task MarkSourceProcessedAsync(string sourceIdHash, CancellationToken cancellationToken = default)
    {
        await TrackMutationAsync(() => Processed.Add(sourceIdHash), cancellationToken);
    }

    public Task RecordReplyObservationAsync(EmailSnapshot email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email.ConversationId) || string.IsNullOrWhiteSpace(email.SenderDisplay))
        {
            return Task.CompletedTask;
        }

        var key = ReplyProgressMatcher.NormalizeParticipantKey(email.SenderDisplay);
        if (ReplyReceipts.Any(receipt => receipt.ConversationId == email.ConversationId && ReplyProgressMatcher.NormalizeParticipantKey(receipt.ParticipantDisplay) == key))
        {
            return Task.CompletedTask;
        }

        ReplyReceipts.Add(new ReplyReceipt(email.ConversationId, email.SenderDisplay, email.ReceivedAt, email.SourceHash));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LocalTaskItem>> ListOpenTasksAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalTaskItem>>(Tasks.Where(task => FollowUpPresentation.IsVisibleInPrimary(task, DateTimeOffset.UtcNow)).ToList());

    public Task<IReadOnlyList<LocalTaskItem>> ListArchivedTasksAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalTaskItem>>(Tasks
            .Where(task => task.Status == LocalTaskStatus.Archived)
            .OrderByDescending(task => task.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList());

    public Task<IReadOnlyList<ReplyProgressItem>> ListReplyProgressAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReplyProgressItem>>(Tasks
            .Select(task => ReplyProgressMatcher.Build(task, ReplyReceipts))
            .Where(progress => progress is not null)
            .Cast<ReplyProgressItem>()
            .ToList());

    public Task<IReadOnlyList<WaitingClosureSuggestion>> ListWaitingClosureSuggestionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WaitingClosureSuggestion>>(ClosureSuggestions
            .Where(suggestion => Tasks.Any(task => task.Id == suggestion.TaskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed))
            .ToList());

    public Task<bool> SaveWaitingClosureSuggestionAsync(WaitingClosureSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        if (ClosureSuggestions.Any(existing =>
            existing.TaskId == suggestion.TaskId
            && existing.TriggerSourceHash == suggestion.TriggerSourceHash
            && existing.TriggerKind == suggestion.TriggerKind))
        {
            return Task.FromResult(false);
        }

        ClosureSuggestions.Add(suggestion);
        return Task.FromResult(true);
    }

    public Task<bool> ResolveWaitingClosureSuggestionAsync(Guid suggestionId, WaitingClosureResolution resolution, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = ClosureSuggestions.FindIndex(suggestion => suggestion.Id == suggestionId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        var suggestion = ClosureSuggestions[index];
        ClosureSuggestions.RemoveAt(index);
        if (resolution == WaitingClosureResolution.Archived)
        {
            var taskIndex = Tasks.FindIndex(task => task.Id == suggestion.TaskId && task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed);
            if (taskIndex < 0)
            {
                return Task.FromResult(false);
            }

            Tasks[taskIndex] = Tasks[taskIndex] with { Status = LocalTaskStatus.Archived, SnoozeUntil = null, UpdatedAt = now };
        }

        return Task.FromResult(true);
    }

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
        Processed.Add(candidate.SourceIdHash);
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
        Processed.Add(candidate.SourceIdHash);
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
        if (!string.IsNullOrWhiteSpace(Tasks[index].SourceIdHash))
        {
            Processed.Add(Tasks[index].SourceIdHash!);
        }
        return Task.FromResult(true);
    }

    public Task<bool> RestoreArchivedTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var index = Tasks.FindIndex(task => task.Id == taskId && task.Status == LocalTaskStatus.Archived);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with { Status = LocalTaskStatus.Open, SnoozeUntil = null, UpdatedAt = now };
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
        if (!string.IsNullOrWhiteSpace(Tasks[index].SourceIdHash))
        {
            Processed.Add(Tasks[index].SourceIdHash!);
        }
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
        if (!string.IsNullOrWhiteSpace(Tasks[index].SourceIdHash))
        {
            Processed.Add(Tasks[index].SourceIdHash!);
        }
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

    private async Task TrackMutationAsync(Action mutate, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeMutations);
        while (true)
        {
            var currentMax = Volatile.Read(ref _maxActiveMutations);
            if (active <= currentMax
                || Interlocked.CompareExchange(ref _maxActiveMutations, active, currentMax) == currentMax)
            {
                break;
            }
        }

        try
        {
            if (MutationDelay > TimeSpan.Zero)
            {
                await Task.Delay(MutationDelay, cancellationToken);
            }

            mutate();
        }
        finally
        {
            Interlocked.Decrement(ref _activeMutations);
        }
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
    private readonly Queue<LlmCompletion> _responses;

    public FakeLlmClient(string response) : this(new LlmCompletion(response))
    {
    }

    public FakeLlmClient(params LlmCompletion[] responses)
    {
        _responses = new Queue<LlmCompletion>(responses.Length == 0
            ? new[] { new LlmCompletion(string.Empty) }
            : responses);
    }

    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPayload { get; private set; }
    public LlmRequestOptions? LastRequestOptions { get; private set; }
    public List<LlmRequestOptions?> RequestOptionsLog { get; } = [];

    public Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPayload = userPayload;
        LastRequestOptions = requestOptions;
        RequestOptionsLog.Add(requestOptions);
        var response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return Task.FromResult(response);
    }
}

sealed class ThrowingLlmClient : ILlmClient
{
    private readonly Exception _exception;

    public ThrowingLlmClient(Exception exception)
    {
        _exception = exception;
    }

    public Task<LlmCompletion> CompleteJsonAsync(
        string systemPrompt,
        string userPayload,
        CancellationToken cancellationToken = default,
        LlmRequestOptions? requestOptions = null) =>
        Task.FromException<LlmCompletion>(_exception);
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

sealed class RecordingBatchAnalyzer : IFollowUpBatchAnalyzer
{
    public RecordingBatchAnalyzer(int preferredBatchSize)
    {
        PreferredBatchSize = preferredBatchSize;
    }

    public int PreferredBatchSize { get; }
    public List<int> BatchSizes { get; } = [];

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default) =>
        Task.FromResult(FollowUpAnalysis.Ignore("single"));

    public Task<IReadOnlyList<FollowUpAnalysis>> AnalyzeBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        BatchSizes.Add(emails.Count);
        return Task.FromResult<IReadOnlyList<FollowUpAnalysis>>(emails
            .Select(_ => FollowUpAnalysis.Ignore("batch"))
            .ToArray());
    }
}

sealed class ConcurrentRecordingBatchAnalyzer : IFollowUpBatchAnalyzer
{
    private readonly TaskCompletionSource _twoActive = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeBatchCalls;
    private int _maxActiveBatchCalls;

    public ConcurrentRecordingBatchAnalyzer(int preferredBatchSize)
    {
        PreferredBatchSize = preferredBatchSize;
    }

    public int PreferredBatchSize { get; }
    public int MaxActiveBatchCalls => Volatile.Read(ref _maxActiveBatchCalls);

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default) =>
        Task.FromResult(FollowUpAnalysis.Ignore("single"));

    public async Task<IReadOnlyList<FollowUpAnalysis>> AnalyzeBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        var active = Interlocked.Increment(ref _activeBatchCalls);
        TrackMaxActive(active);
        if (active >= 2)
        {
            _twoActive.TrySetResult();
        }

        try
        {
            await Task.WhenAny(_twoActive.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            return emails.Select(_ => FollowUpAnalysis.Ignore("batch")).ToArray();
        }
        finally
        {
            Interlocked.Decrement(ref _activeBatchCalls);
        }
    }

    private void TrackMaxActive(int active)
    {
        while (true)
        {
            var currentMax = Volatile.Read(ref _maxActiveBatchCalls);
            if (active <= currentMax
                || Interlocked.CompareExchange(ref _maxActiveBatchCalls, active, currentMax) == currentMax)
            {
                return;
            }
        }
    }
}

sealed class CancellableBatchAnalyzer : IFollowUpBatchAnalyzer
{
    private readonly TaskCompletionSource _startedEnough = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _preferredBatchSize;
    private int _startedBatchCalls;

    public CancellableBatchAnalyzer(int preferredBatchSize)
    {
        _preferredBatchSize = preferredBatchSize;
    }

    public int PreferredBatchSize => _preferredBatchSize;
    public int StartedBatchCalls => Volatile.Read(ref _startedBatchCalls);

    public Task<FollowUpAnalysis> AnalyzeAsync(EmailSnapshot email, CancellationToken cancellationToken = default) =>
        Task.FromResult(FollowUpAnalysis.Ignore("single"));

    public async Task<IReadOnlyList<FollowUpAnalysis>> AnalyzeBatchAsync(IReadOnlyList<EmailSnapshot> emails, CancellationToken cancellationToken = default)
    {
        var started = Interlocked.Increment(ref _startedBatchCalls);
        if (started >= 2)
        {
            _startedEnough.TrySetResult();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return emails.Select(_ => FollowUpAnalysis.Ignore("batch")).ToArray();
    }

    public async Task WaitForStartedAsync(int expectedStarted, CancellationToken cancellationToken)
    {
        if (StartedBatchCalls >= expectedStarted)
        {
            return;
        }

        await _startedEnough.Task.WaitAsync(cancellationToken);
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
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        };
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

sealed class HydratingSequenceEmailSource : IEmailHydratingSource
{
    private readonly IReadOnlyList<EmailSnapshot> _metadataMessages;
    private readonly IReadOnlyDictionary<string, EmailSnapshot> _hydratedBySourceId;
    private readonly IReadOnlySet<string> _throwingSourceIds;

    public HydratingSequenceEmailSource(
        IReadOnlyList<EmailSnapshot> metadataMessages,
        IReadOnlyDictionary<string, EmailSnapshot> hydratedBySourceId,
        IReadOnlySet<string>? throwingSourceIds = null)
    {
        _metadataMessages = metadataMessages;
        _hydratedBySourceId = hydratedBySourceId;
        _throwingSourceIds = throwingSourceIds ?? new HashSet<string>();
    }

    public int HydrateCalls { get; private set; }

    public Task<EmailReadResult> ReadAsync(MailReadRequest request, CancellationToken cancellationToken = default)
    {
        var messages = request.MaxItems <= 0 ? _metadataMessages : _metadataMessages.Take(request.MaxItems).ToArray();
        return Task.FromResult(new EmailReadResult(messages.ToArray(), Array.Empty<MailReadWarning>(), 0));
    }

    public Task<EmailSnapshot?> TryReadBySourceIdAsync(string? sourceId, CancellationToken cancellationToken = default)
    {
        HydrateCalls++;
        if (sourceId is not null && _throwingSourceIds.Contains(sourceId))
        {
            throw new InvalidOperationException("Hydration failed.");
        }

        return Task.FromResult(sourceId is not null && _hydratedBySourceId.TryGetValue(sourceId, out var snapshot) ? snapshot : null);
    }
}


sealed class FakeInventorySource : IMailMirrorInventorySource
{
    private readonly IReadOnlyList<MailInventoryItem> _items;

    public FakeInventorySource(IReadOnlyList<MailInventoryItem> items)
    {
        _items = items;
    }

    public List<MailMirrorLocator> HydrateCalls { get; } = new();
    public HashSet<MailMirrorLocator> FailHydrationFor { get; } = new();
    public HashSet<MailSourceFolder> IncompleteFolders { get; } = new();
    public HashSet<MailSourceFolder> WarningFolders { get; } = new();
    public int? CancelHydrationAfter { get; set; }

    public async IAsyncEnumerable<MailInventoryPage> EnumerateAsync(
        MailInventoryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = _items
            .Where(item => item.Folder == request.Folder)
            .Where(item => request.Checkpoint is null || MailMirrorCursor.IsAfter(request.Checkpoint, item))
            .OrderBy(item => item.LastModifiedAt)
            .ThenBy(item => item.StoreId, StringComparer.Ordinal)
            .ThenBy(item => item.EntryId, StringComparer.Ordinal)
            .Take(request.PageSize)
            .ToArray();
        await Task.Yield();
        var warnings = WarningFolders.Contains(request.Folder)
            ? new[] { new MailMirrorSyncWarning("fake-inventory-warning", CapabilitySeverity.Degraded, "FakeWarning") }
            : null;
        yield return new MailInventoryPage(
            request.Folder,
            page,
            page.Length == 0 ? request.Checkpoint : page[^1].Cursor,
            Completed: !IncompleteFolders.Contains(request.Folder),
            Warnings: warnings);
    }

    public Task<MailMirrorMessage?> HydrateAsync(MailInventoryItem item, CancellationToken cancellationToken = default)
    {
        if (CancelHydrationAfter is not null && HydrateCalls.Count >= CancelHydrationAfter.Value)
        {
            throw new OperationCanceledException();
        }

        HydrateCalls.Add(item.Locator);
        if (FailHydrationFor.Contains(item.Locator))
        {
            throw new InvalidOperationException("fake failure");
        }

        return Task.FromResult<MailMirrorMessage?>(new MailMirrorMessage(
            item.StoreId,
            item.EntryId,
            item.Folder,
            item.LastModifiedAt,
            item.Subject,
            item.SenderDisplay,
            item.Subject + " body",
            item.ReceivedAt,
            item.SentAt,
            item.ConversationId,
            item.RecipientDisplayNames));
    }
}
