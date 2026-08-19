using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
using MailWhere.Core.Search;
using MailWhere.Storage;
using MailWhere.OutlookCom;

namespace MailWhere.Windows;

public partial class MainWindow : Window
{
    private SqliteFollowUpStore? _store;
    private IUserNotificationSink _notificationSink = new NullNotificationSink();
    private readonly NotificationThrottle _notificationThrottle = new();
    private RuntimeSettings _settings;
    private DispatcherTimer? _reminderTimer;
    private DispatcherTimer? _dailyBoardTimer;
    private DispatcherTimer? _automaticScanTimer;
    private DispatcherTimer? _eventScanDebounceTimer;
    private OutlookItemAddWatcher? _outlookItemAddWatcher;
    private CancellationTokenSource? _scanCancellationSource;
    private readonly DateTimeOffset _appStartedAt = DateTimeOffset.Now;
    private bool _scanInProgress;
    private bool _fallbackPromptShownThisSession;
    private bool _backgroundStarted;
    private bool _allowExit;
    private ReviewCandidatesWindow? _reviewCandidatesWindow;
    private MailSearchWindow? _mailSearchWindow;
    private ArchiveWindow? _archiveWindow;
    private SettingsWindow? _settingsWindow;
    private BoardSnapshot? _boardSnapshot;
    private BoardRouteFilter _mainFilter = BoardRouteFilter.Week;
    private AnalysisTelemetry _lastAnalysisTelemetry = AnalysisTelemetry.Empty;
    private bool _dailyBoardCheckInProgress;
    private bool _reminderCheckInProgress;
    private bool _eventScanPending;

    public MainWindow()
    {
        InitializeComponent();
        _settings = UpgradeRuntimeSettings(WindowsRuntimeSettingsStore.Load());
        Closing += MainWindow_Closing;
    }

    private static RuntimeSettings UpgradeRuntimeSettings(RuntimeSettings settings) =>
        settings.ExternalLlmEnabled && settings.LlmTimeoutSeconds == 30
            ? settings with { LlmTimeoutSeconds = RuntimeSettings.ManagedSafeDefault.LlmTimeoutSeconds }
            : settings;

    public void SetNotificationSink(IUserNotificationSink notificationSink)
    {
        _notificationSink = notificationSink;
    }

    public async Task StartBackgroundAsync()
    {
        if (_backgroundStarted)
        {
            return;
        }

        _backgroundStarted = true;
        await OnLoadedAsync();
    }

    public void ShowShell(bool refresh = true)
    {
        if (refresh)
        {
            _ = RefreshTasksSafelyAsync();
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task RefreshTasksSafelyAsync()
    {
        try
        {
            await RefreshTasksAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"업무 목록 새로고침 실패: {ex.GetType().Name}";
        }
    }

    public void ReportStatus(string message) => StatusText.Text = message;

    public void AllowExit() => _allowExit = true;

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            StopOutlookEventWatcher();
            return;
        }

        e.Cancel = true;
        Hide();
        StatusText.Text = "창을 닫아도 트레이에서 계속 실행됩니다.";
    }

    private async void ScanRecentMonth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ScanRecentMailAsync(showSummaryNotification: true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("메일 확인 실패", ex);
        }
    }

    private void StopScan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellationSource is null || _scanCancellationSource.IsCancellationRequested)
        {
            return;
        }

        _scanCancellationSource.Cancel();
        StatusText.Text = "메일 확인 중지를 요청했습니다. 현재 작업이 정리되면 멈춥니다.";
        ScanProgressText.Text = "중지 요청됨…";
    }

    public async Task OpenDailyBoardAsync()
    {
        await OpenDailyBoardAsync(DailyBoardOpenOptions.ManualWeek());
    }

    public async Task OpenDailyBoardTodayAsync(bool showBriefSummary, BoardOrigin origin)
    {
        await OpenDailyBoardAsync(new DailyBoardOpenOptions(
            BoardRouteFilter.Today,
            showBriefSummary,
            origin,
            BringToFront: true));
    }

    private async Task OpenDailyBoardAsync(DailyBoardOpenOptions options)
    {
        await ShowUnifiedBoardAsync(options);
    }

    public void OpenReviewTab()
    {
        ShowShell();
        _ = OpenReviewCandidatesWindowAsync();
    }

    private async Task ShowUnifiedBoardAsync(DailyBoardOpenOptions options)
    {
        _mainFilter = options.Filter == BoardRouteFilter.Month ? BoardRouteFilter.Week : options.Filter;
        await RefreshTasksAsync();
        ShowShell(refresh: false);
        BringWindowToFront(this);
        StatusText.Text = options.ShowBriefSummary
            ? "오늘 업무를 열었습니다."
            : "업무 보드를 열었습니다.";
    }

    private async void TodayMainFilter_Click(object sender, RoutedEventArgs e) => await SetMainFilterAsync(BoardRouteFilter.Today);
    private async void WeekMainFilter_Click(object sender, RoutedEventArgs e) => await SetMainFilterAsync(BoardRouteFilter.Week);
    private async void NoDueMainFilter_Click(object sender, RoutedEventArgs e) => await SetMainFilterAsync(BoardRouteFilter.NoDue);
    private async void AllMainFilter_Click(object sender, RoutedEventArgs e) => await SetMainFilterAsync(BoardRouteFilter.All);

    private async Task SetMainFilterAsync(BoardRouteFilter filter)
    {
        _mainFilter = filter;
        if (_boardSnapshot is null)
        {
            await RefreshTasksAsync();
            return;
        }

        RenderTasks(_boardSnapshot);
    }

    private async void OpenTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetTaskListItem(sender) is { Task: { } task })
            {
                await OpenTaskSourceAsync(task);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"원본 메일을 열지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void ArchiveTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetTaskListItem(sender) is { Task: { } task })
            {
                await ArchiveTaskAsync(task);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"업무를 보관하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void TaskList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is DependencyObject source
                && FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is null
                && FindVisualAncestor<ListBoxItem>(source)?.DataContext is TaskListItem { Task: { } task })
            {
                e.Handled = true;
                await EditTaskAsync(task);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"업무를 수정하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void SetTaskDueButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetTaskListItem(sender) is not { Task: { } task })
            {
                return;
            }

            var dialog = new DueDateDialog(DateTime.Today, task.DueAt?.DateTime)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true && dialog.SelectedDueAt is { } dueAt)
            {
                await SetTaskDueAsync(task, dueAt);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"기한을 설정하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void SnoozeTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || GetTaskListItem(sender) is not { Task: { } task })
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        AddSnoozeMenuItem(menu, "오늘 1시에 다시 보기", task, SnoozePlanner.Plan(SnoozePreset.TodayAtOnePm, DateTimeOffset.Now));
        AddSnoozeMenuItem(menu, "내일 아침 다시 보기", task, SnoozePlanner.Plan(SnoozePreset.TomorrowMorning, DateTimeOffset.Now));
        AddSnoozeMenuItem(menu, "다음 월요일 다시 보기", task, SnoozePlanner.Plan(SnoozePreset.NextMondayMorning, DateTimeOffset.Now));
        var custom = new System.Windows.Controls.MenuItem { Header = "직접 날짜 선택" };
        custom.Click += async (_, _) =>
        {
            var dialog = new DueDateDialog(DateTime.Today, task.SnoozeUntil?.DateTime ?? task.DueAt?.DateTime)
            {
                Owner = this,
                Title = "나중에 보기"
            };
            if (dialog.ShowDialog() == true && dialog.SelectedDueAt is { } until)
            {
                await SnoozeTaskAsync(task, until);
            }
        };
        menu.Items.Add(custom);
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void AddSnoozeMenuItem(System.Windows.Controls.ContextMenu menu, string header, LocalTaskItem task, DateTimeOffset until)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += async (_, _) => await SnoozeTaskAsync(task, until);
        menu.Items.Add(item);
    }

    private async void AddManualTaskDialog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ManualTaskDialog(DateTime.Today)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await CreateManualTaskAsync(dialog.TaskTitle, dialog.DueAt);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("직접 추가 실패", ex);
        }
    }

    private async void OpenReviewCandidates_Click(object sender, RoutedEventArgs e)
    {
        await OpenReviewCandidatesWindowAsync();
    }

    private void OpenMailSearch_Click(object sender, RoutedEventArgs e) => OpenMailSearchWindow();

    private async void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        await OpenArchiveWindowAsync();
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        await OpenSettingsWindowAsync();
    }

    private async Task OnLoadedAsync()
    {
        await RefreshTasksAsync();
        await NotifyDueRemindersAsync();
        StartReminderTimer();

        if (_settings.AutomaticWatcherRequested && _settings.SmokeGatePassed)
        {
            StartOutlookEventWatcher();
            await ScanRecentMailAsync(showSummaryNotification: false);
            StartAutomaticScanTimer();
        }

        StartDailyBoardTimer();
        await MaybeShowDailyBoardAsync();
    }

    private async Task<MailScanSummary> ScanRecentMailAsync(bool showSummaryNotification)
    {
        if (_scanInProgress)
        {
            return new MailScanSummary(0, 0, 0, 0, 0, 0, Array.Empty<MailReadWarning>());
        }

        _scanInProgress = true;
        _scanCancellationSource?.Dispose();
        _scanCancellationSource = new CancellationTokenSource();
        var scanCancellationToken = _scanCancellationSource.Token;
        SetScanBusy(true, "메일 확인 준비 중입니다…");
        try
        {
            StatusText.Text = $"최근 {_settings.RecentScanDays}일 메일을 읽고 업무를 찾는 중입니다…";
            await Dispatcher.Yield(DispatcherPriority.Background);

            if (LlmAnalysisEnabled(_settings) && !_settings.HasCurrentLlmProbeProof())
            {
                StatusText.Text = "AI 분석 연결 테스트를 먼저 통과해야 메일 확인을 시작합니다.";
                return new MailScanSummary(0, 0, 0, 0, 0, 0, new[] { new MailReadWarning("llm-probe-required", CapabilitySeverity.Blocked, "LlmProbeRequired") });
            }

            var store = await GetStoreAsync();
            var beforeCandidateIds = (await store.ListReviewCandidatesAsync())
                .Select(candidate => candidate.Id)
                .ToHashSet();
            var mirrorSummary = await RunMailMirrorSyncAsync(store, showSummaryNotification, scanCancellationToken);
            var analyzer = BuildAnalyzer(_settings);
            var pipeline = new FollowUpPipeline(analyzer, store, waitingClosureJudge: BuildWaitingClosureJudge(_settings));
            var scanner = new MailActionScanner(new OutlookComMailSource(), pipeline);
            var now = DateTimeOffset.Now;
            var scanStartedAt = now;
            var windowPlan = await PlanIncrementalScanWindowAsync(store, now, scanCancellationToken);
            StatusText.Text = $"{BuildScanWindowText(windowPlan)}을 읽고 업무를 찾는 중입니다…";
            var request = new MailScanRequest(
                _settings.RecentScanMaxItems,
                IncludeBody: true,
                windowPlan.Since,
                _settings.LlmInitialConcurrency,
                _settings.LlmMaxConcurrency,
                InboxSince: windowPlan.InboxSince,
                SentSince: windowPlan.SentSince,
                UseFastFilter: true);
            var progress = new Progress<MailScanProgress>(UpdateScanProgress);

            var summary = await scanner.ScanAsync(request, progress, scanCancellationToken);
            if (!summary.Warnings.Any(warning => warning.Severity == CapabilitySeverity.Blocked))
            {
                await RecordSuccessfulScanCursorAsync(store, summary.Warnings, scanStartedAt, scanCancellationToken);
            }
            _lastAnalysisTelemetry = analyzer is IAnalysisTelemetrySource telemetrySource
                ? telemetrySource.GetTelemetrySnapshot()
                : AnalysisTelemetry.Empty;
            var smokeGateRecorded = showSummaryNotification && MarkSmokeGatePassedAfterManualScan(summary);
            if (showSummaryNotification && smokeGateRecorded)
            {
                StatusText.Text = "수동 확인이 성공해 자동 메일 확인을 켤 수 있습니다.";
                if (_settings.AutomaticWatcherRequested)
                {
                    StartOutlookEventWatcher();
                    StartAutomaticScanTimer();
                }
            }

            await RefreshTasksAsync();
            var reviewCandidates = _boardSnapshot?.Candidates ?? Array.Empty<ReviewCandidate>();
            _reviewCandidatesWindow?.Refresh(
                reviewCandidates,
                _boardSnapshot?.ClosureSuggestions ?? Array.Empty<WaitingClosureSuggestion>(),
                CanRetryLlmFailures,
                _boardSnapshot?.ReviewCounts ?? new ReviewCandidateBacklogCounts(reviewCandidates.Count, reviewCandidates.Count, reviewCandidates.Count(candidate => candidate.Analysis.IsTransientLlmFailureReview)));
            if (!showSummaryNotification)
            {
                await NotifyDueRemindersAsync();
            }
            var newReviewCandidateCount = reviewCandidates.Count(candidate => !beforeCandidateIds.Contains(candidate.Id));

            var llmSummary = _lastAnalysisTelemetry.ToKoreanSummary();
            var mirrorStatus = BuildMirrorSummaryText(mirrorSummary);
            var scanWindowText = BuildScanWindowText(windowPlan);
            StatusText.Text = $"{scanWindowText} {summary.ReadCount}건 확인 · 할 일 {summary.TaskCreatedCount}건 · 확인 필요 {newReviewCandidateCount}건 · 중복 {summary.DuplicateCount}건 · {mirrorStatus} · {llmSummary}"
                + (smokeGateRecorded ? " · 자동 확인 준비 완료" : string.Empty);
            if (showSummaryNotification
                && !ShouldSuppressPopupNotifications()
                && (summary.TaskCreatedCount > 0 || newReviewCandidateCount > 0))
            {
                await _notificationSink.ShowAsync(new UserNotification(
                    UserNotificationKind.ScanSummary,
                    "메일 확인 완료",
                    $"할 일 {summary.TaskCreatedCount}건, 확인 필요 {newReviewCandidateCount}건을 찾았습니다. 확인 필요 항목은 MailWhere에서 확인하세요.",
                    "scan-summary"));
            }
            if (_lastAnalysisTelemetry.LlmFailureCount > 0)
            {
                OfferRuleFallbackAfterLlmFailure();
            }

            return summary;
        }
        catch (OperationCanceledException) when (scanCancellationToken.IsCancellationRequested)
        {
            StatusText.Text = "사용자 요청으로 메일 확인을 중지했습니다. 이미 처리된 항목은 유지됩니다.";
            ScanProgressText.Text = "메일 확인 중지됨";
            return new MailScanSummary(0, 0, 0, 0, 0, 0, Array.Empty<MailReadWarning>());
        }
        finally
        {
            _scanCancellationSource?.Dispose();
            _scanCancellationSource = null;
            _scanInProgress = false;
            SetScanBusy(false, "대기 중입니다.");
        }
    }


    private async Task<MailMirrorSyncSummary> RunMailMirrorSyncAsync(
        SqliteFollowUpStore store,
        bool manualRequested,
        CancellationToken cancellationToken)
    {
        var initialSyncCompletedAt = await store.GetAppStateAsync(MailMirrorSyncCadencePolicy.InitialSyncCompletedAtStateKey, cancellationToken);
        var lastAuthoritativeReconcileAt = await store.GetAppStateAsync(MailMirrorSyncCadencePolicy.LastAuthoritativeReconcileAtStateKey, cancellationToken);
        var cadence = MailMirrorSyncCadencePolicy.Select(DateTimeOffset.UtcNow, manualRequested, initialSyncCompletedAt, lastAuthoritativeReconcileAt);

        StatusText.Text = cadence switch
        {
            MailMirrorSyncCadence.Authoritative => "메일 검색 인덱스를 전체 기준으로 맞추는 중입니다…",
            MailMirrorSyncCadence.Incremental => "최근 변경 메일을 검색 인덱스에 반영하는 중입니다…",
            _ => "메일 검색 인덱스를 처음 준비하는 중입니다…"
        };
        await Dispatcher.Yield(DispatcherPriority.Background);

        await using var mirrorStore = new SqliteMailMirrorStore(GetDatabasePath());
        await mirrorStore.InitializeAsync(cancellationToken);
        var service = new MailMirrorBackfillService(new OutlookComMailInventorySource(), mirrorStore);
        var progress = new Progress<MailMirrorSyncProgress>(UpdateMailMirrorProgress);
        var summary = cadence == MailMirrorSyncCadence.Authoritative
            ? await service.RunAuthoritativeReconcileAsync(progress, cancellationToken)
            : await service.RunInitialBackfillAsync(progress, cancellationToken);

        if (MailMirrorSyncCadencePolicy.IsWarningFree(summary))
        {
            var completedAt = DateTimeOffset.UtcNow.ToString("O");
            if (cadence == MailMirrorSyncCadence.Initial)
            {
                await store.SetAppStateAsync(MailMirrorSyncCadencePolicy.InitialSyncCompletedAtStateKey, completedAt, cancellationToken);
            }

            if (cadence is MailMirrorSyncCadence.Initial or MailMirrorSyncCadence.Authoritative)
            {
                await store.SetAppStateAsync(MailMirrorSyncCadencePolicy.LastAuthoritativeReconcileAtStateKey, completedAt, cancellationToken);
            }
        }

        return summary;
    }

    private void UpdateMailMirrorProgress(MailMirrorSyncProgress progress)
    {
        ScanProgressText.Text = $"메일 검색 인덱스: {progress.Folder} {progress.SeenCount}건 확인 · {progress.HydratedCount}건 반영";
    }

    private static string BuildMirrorSummaryText(MailMirrorSyncSummary summary)
    {
        var warningText = summary.Warnings.Count == 0 ? "완료" : $"주의 {summary.Warnings.Count}건";
        return $"검색 인덱스 {summary.SeenCount}건 확인/{summary.HydratedCount}건 저장 {warningText}";
    }

    private async Task<AutomaticScanWindowPlan> PlanIncrementalScanWindowAsync(SqliteFollowUpStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var inboxLastSuccessfulScan = await store.GetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulInboxScanStateKey, cancellationToken);
        var sentLastSuccessfulScan = await store.GetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulSentScanStateKey, cancellationToken);
        var lastSuccessfulScan = await store.GetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulScanStateKey, cancellationToken);
        return AutomaticScanWindowPlanner.PlanFolders(now, _settings.RecentScanDays, inboxLastSuccessfulScan, sentLastSuccessfulScan, lastSuccessfulScan);
    }

    private async Task RecordSuccessfulScanCursorAsync(
        SqliteFollowUpStore store,
        IReadOnlyList<MailReadWarning> warnings,
        DateTimeOffset scanStartedAt,
        CancellationToken cancellationToken)
    {
        var cursorValue = scanStartedAt.ToString("O");
        if (FolderScanSucceeded(warnings, MailSourceFolder.Inbox))
        {
            await store.SetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulInboxScanStateKey, cursorValue, cancellationToken);
        }

        if (FolderScanSucceeded(warnings, MailSourceFolder.Sent))
        {
            await store.SetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulSentScanStateKey, cursorValue, cancellationToken);
        }

        await store.SetAppStateAsync(AutomaticScanWindowPlanner.LastSuccessfulScanStateKey, cursorValue, cancellationToken);
    }

    private string BuildScanWindowText(AutomaticScanWindowPlan windowPlan)
    {
        if (windowPlan.UsedLastSuccessfulScan)
        {
            return "최근 변경 메일";
        }

        if (windowPlan.UsedInboxLastSuccessfulScan || windowPlan.UsedSentLastSuccessfulScan)
        {
            return "최근 변경+누락 보정 메일";
        }

        return $"최근 {_settings.RecentScanDays}일 메일";
    }

    private static bool FolderScanSucceeded(IReadOnlyList<MailReadWarning> warnings, MailSourceFolder folder)
    {
        if (warnings.Any(warning => warning.Severity == CapabilitySeverity.Blocked))
        {
            return false;
        }

        var folderCode = folder == MailSourceFolder.Sent ? "sent" : "inbox";
        return !warnings.Any(warning =>
            warning.Code.Equals($"outlook-{folderCode}-read-failed", StringComparison.OrdinalIgnoreCase)
            || warning.Code.Equals($"outlook-{folderCode}-folder-unavailable", StringComparison.OrdinalIgnoreCase)
            || (folder == MailSourceFolder.Sent && warning.Code.Equals("outlook-sent-folder-unavailable", StringComparison.OrdinalIgnoreCase)));
    }

    private void StartReminderTimer()
    {
        if (_reminderTimer is not null)
        {
            return;
        }

        _reminderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _reminderTimer.Tick += async (_, _) =>
        {
            if (_reminderCheckInProgress)
            {
                return;
            }

            _reminderCheckInProgress = true;
            try
            {
                await NotifyDueRemindersAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"리마인더 점검 실패: {ex.GetType().Name}";
            }
            finally
            {
                _reminderCheckInProgress = false;
            }
        };
        _reminderTimer.Start();
    }

    private void StartDailyBoardTimer()
    {
        if (_dailyBoardTimer is not null)
        {
            return;
        }

        _dailyBoardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _dailyBoardTimer.Tick += async (_, _) =>
        {
            if (_dailyBoardCheckInProgress)
            {
                return;
            }

            _dailyBoardCheckInProgress = true;
            try
            {
                await MaybeShowDailyBoardAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오늘의 업무 보드 점검 실패: {ex.GetType().Name}";
            }
            finally
            {
                _dailyBoardCheckInProgress = false;
            }
        };
        _dailyBoardTimer.Start();
    }

    private void StartAutomaticScanTimer()
    {
        if (_automaticScanTimer is not null)
        {
            _automaticScanTimer.Interval = TimeSpan.FromMinutes(_settings.AutomaticScanIntervalMinutes);
            return;
        }

        _automaticScanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.AutomaticScanIntervalMinutes)
        };
        _automaticScanTimer.Tick += async (_, _) =>
        {
            if (!_settings.AutomaticWatcherRequested || !_settings.SmokeGatePassed)
            {
                return;
            }

            try
            {
                await ScanRecentMailAsync(showSummaryNotification: false);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"자동 메일 확인 실패: {ex.GetType().Name}";
            }
        };
        _automaticScanTimer.Start();
    }

    private void StartOutlookEventWatcher()
    {
        if (_outlookItemAddWatcher is not null)
        {
            return;
        }

        try
        {
            _outlookItemAddWatcher = OutlookItemAddWatcher.Start();
            _outlookItemAddWatcher.ItemAdded += OutlookItemAddWatcher_ItemAdded;
            StatusText.Text = "새 메일 이벤트 감지를 시작했습니다. 놓친 항목은 자동 확인 간격으로 보정합니다.";
        }
        catch (Exception ex)
        {
            _outlookItemAddWatcher = null;
            StatusText.Text = $"새 메일 이벤트 감지를 사용할 수 없어 자동 확인 간격으로 보정합니다: {ex.GetType().Name}";
        }
    }

    private void StopOutlookEventWatcher()
    {
        if (_outlookItemAddWatcher is not null)
        {
            _outlookItemAddWatcher.ItemAdded -= OutlookItemAddWatcher_ItemAdded;
            _outlookItemAddWatcher.Dispose();
            _outlookItemAddWatcher = null;
        }

        _eventScanDebounceTimer?.Stop();
        _eventScanPending = false;
    }

    private void OutlookItemAddWatcher_ItemAdded(object? sender, OutlookItemAddedEvent e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OutlookItemAddWatcher_ItemAdded(sender, e));
            return;
        }

        if (!_settings.AutomaticWatcherRequested || !_settings.SmokeGatePassed)
        {
            return;
        }

        _eventScanPending = true;
        _eventScanDebounceTimer ??= CreateEventScanDebounceTimer();
        _eventScanDebounceTimer.Stop();
        _eventScanDebounceTimer.Start();
        StatusText.Text = e.SourceFolder == MailSourceFolder.Sent
            ? "보낸 메일 변화를 감지했습니다. 곧 확인합니다…"
            : "새 메일을 감지했습니다. 곧 확인합니다…";
    }

    private DispatcherTimer CreateEventScanDebounceTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (!_eventScanPending || !_settings.AutomaticWatcherRequested || !_settings.SmokeGatePassed)
            {
                return;
            }

            if (_scanInProgress)
            {
                timer.Start();
                return;
            }

            _eventScanPending = false;
            try
            {
                await ScanRecentMailAsync(showSummaryNotification: false);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"새 메일 이벤트 확인 실패: {ex.GetType().Name}";
            }
        };
        return timer;
    }

    private async Task RefreshTasksAsync()
    {
        _boardSnapshot = await LoadBoardSnapshotAsync();
        RenderTasks(_boardSnapshot);
    }

    private async Task<BoardSnapshot> LoadBoardSnapshotAsync()
    {
        var store = await GetStoreAsync();
        var tasks = await store.ListOpenTasksAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        var reviewCounts = await store.CountReviewCandidateBacklogAsync(candidates.Count);
        var replyProgress = (await store.ListReplyProgressAsync()).ToDictionary(progress => progress.TaskId);
        var closureSuggestions = await store.ListWaitingClosureSuggestionsAsync();
        return new BoardSnapshot(tasks, candidates, reviewCounts, replyProgress, closureSuggestions);
    }

    private void RenderTasks(BoardSnapshot snapshot)
    {
        TasksList.Items.Clear();
        var now = DateTimeOffset.Now;
        var visible = DailyBoardRouteTaskSelector.SelectVisibleTasks(
                snapshot.Tasks,
                snapshot.Candidates,
                now,
                _mainFilter,
                showBriefSummary: false)
            .OrderBy(SortKey)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        TasksList.Visibility = visible.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TasksEmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var task in visible)
        {
            snapshot.ReplyProgress.TryGetValue(task.Id, out var progress);
            TasksList.Items.Add(TaskListItem.FromTask(task, now, progress));
        }

        UpdateReviewCandidatesButton(snapshot.ReviewCounts, snapshot.ClosureSuggestions.Count);
        UpdateMainFilterHighlight();
    }

    private void UpdateReviewCandidatesButton(ReviewCandidateBacklogCounts reviewCounts, int closureSuggestionCount)
    {
        var total = reviewCounts.TotalUnresolved + closureSuggestionCount;
        OpenReviewCandidatesButton.Content = total == 0
            ? "확인 필요"
            : $"확인 필요 {total}";
        OpenReviewCandidatesButton.ToolTip = $"전체 미해결 {reviewCounts.TotalUnresolved}개 · 현재 표시 {reviewCounts.VisiblePageCount}개(최대 100개) · 재시도 가능 AI 실패 {reviewCounts.RetryableLlmFailures}개"
            + (closureSuggestionCount > 0 ? $" · 보관 제안 {closureSuggestionCount}개" : string.Empty);
    }

    private static DateTimeOffset SortKey(LocalTaskItem task) =>
        task.DueAt ?? task.SnoozeUntil ?? DateTimeOffset.MaxValue;

    private void UpdateMainFilterHighlight()
    {
        SetFilterStyle(TodayFilterButton, _mainFilter == BoardRouteFilter.Today);
        SetFilterStyle(WeekFilterButton, _mainFilter == BoardRouteFilter.Week);
        SetFilterStyle(NoDueFilterButton, _mainFilter == BoardRouteFilter.NoDue);
        SetFilterStyle(AllFilterButton, _mainFilter == BoardRouteFilter.All);
    }

    private static void SetFilterStyle(System.Windows.Controls.Button button, bool active)
    {
        button.Background = active
            ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEE, 0xF3, 0xFF));
        button.BorderBrush = active
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x58, 0xF2))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD8, 0xE2, 0xFF));
    }

    private async Task<ReviewCandidateBacklogCounts> CountReviewCandidateBacklogAsync(int visiblePageCount)
    {
        var store = await GetStoreAsync();
        return await store.CountReviewCandidateBacklogAsync(visiblePageCount);
    }

    private async Task<IReadOnlyList<ReviewCandidate>> RefreshReviewCandidatesAsync()
    {
        var store = await GetStoreAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        var counts = await store.CountReviewCandidateBacklogAsync(candidates.Count);
        var suggestions = await store.ListWaitingClosureSuggestionsAsync();
        _reviewCandidatesWindow?.Refresh(candidates, suggestions, CanRetryLlmFailures, counts);
        if (_boardSnapshot is not null)
        {
            _boardSnapshot = _boardSnapshot with { Candidates = candidates, ReviewCounts = counts, ClosureSuggestions = suggestions };
            UpdateReviewCandidatesButton(_boardSnapshot.ReviewCounts, _boardSnapshot.ClosureSuggestions.Count);
        }
        else
        {
            UpdateReviewCandidatesButton(counts, suggestions.Count);
        }
        return candidates;
    }

    private async Task<IReadOnlyList<WaitingClosureSuggestion>> RefreshClosureSuggestionsAsync()
    {
        var store = await GetStoreAsync();
        var suggestions = await store.ListWaitingClosureSuggestionsAsync();
        if (_boardSnapshot is not null)
        {
            _boardSnapshot = _boardSnapshot with { ClosureSuggestions = suggestions };
            UpdateReviewCandidatesButton(_boardSnapshot.ReviewCounts, _boardSnapshot.ClosureSuggestions.Count);
        }
        else
        {
            var counts = await store.CountReviewCandidateBacklogAsync(0);
            UpdateReviewCandidatesButton(counts, suggestions.Count);
        }

        if (_reviewCandidatesWindow?.IsVisible == true)
        {
            var candidates = _boardSnapshot?.Candidates ?? await store.ListReviewCandidatesAsync();
            _reviewCandidatesWindow.Refresh(candidates, suggestions, CanRetryLlmFailures, await CountReviewCandidateBacklogAsync(candidates.Count));
        }

        return suggestions;
    }

    private static string CompactLine(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxChars ? compact : compact[..maxChars].TrimEnd() + "…";
    }

    private async Task OpenSourceMailAsync(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            StatusText.Text = "이 항목은 원본 메일 연결 정보가 없습니다. 새 버전에서 다시 확인한 항목부터 열 수 있습니다.";
            return;
        }

        try
        {
            StatusText.Text = "Outlook에서 원본 메일을 여는 중입니다…";
            var result = await new OutlookComMailOpener().OpenAsync(sourceId);
            StatusText.Text = result.Success ? result.Message : $"원본 메일 열기 실패: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"원본 메일 열기 실패: {ex.GetType().Name}";
        }
    }

    private async Task MaybeShowDailyBoardAsync()
    {
        var store = await GetStoreAsync();
        var now = DateTimeOffset.Now;
        var lastShownDate = await store.GetAppStateAsync(DailyBoardPlanner.LastShownDateKey);
        var plan = DailyBoardPlanner.Plan(
            now,
            _settings.DailyBoardTime,
            lastShownDate,
            _appStartedAt,
            TimeSpan.FromMinutes(_settings.DailyBoardStartupDelayMinutes));
        if (!plan.ShouldShowNow)
        {
            return;
        }

        try
        {
            await ShowDailyBoardAsync(now, plan.DailyBoardTime, DailyBoardOpenOptions.TodayBrief(BoardOrigin.ScheduledDailyBoard));
            await store.SetAppStateAsync(DailyBoardPlanner.LastShownDateKey, plan.TodayKey);
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-board-scheduled-opened", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey,
                ["surface"] = "window"
            });
            StatusText.Text = "오늘 업무 보드를 열었습니다. 트레이에서 다시 볼 수 있습니다.";
        }
        catch (OperationCanceledException)
        {
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-board-scheduled-open-canceled-not-marked", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey
            });
            StatusText.Text = "오늘 업무 보드 열기가 취소되어 오늘 표시로 기록하지 않았습니다.";
        }
        catch (Exception ex)
        {
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-board-scheduled-open-failed", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey,
                ["errorClass"] = ex.GetType().Name
            });
            await TryDailyBriefNotificationFallbackAsync(store, plan, now, ex);
        }
    }

    private async Task TryDailyBriefNotificationFallbackAsync(SqliteFollowUpStore store, DailyBoardPlan plan, DateTimeOffset now, Exception boardException)
    {
        try
        {
            var tasks = await store.ListOpenTasksAsync();
            var candidates = await store.ListReviewCandidatesAsync();
            var snapshot = DailyBriefPlanner.Build(tasks, candidates, now);
            await DailyBriefNotificationEmitter.EmitAndMarkShownAsync(_notificationSink, store, plan, snapshot);
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-brief-notification-fallback-emitted", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey,
                ["boardErrorClass"] = boardException.GetType().Name,
                ["surface"] = "notification"
            });
            StatusText.Text = "업무 보드를 바로 열지 못해 오늘 브리핑 알림으로 대신 안내했습니다.";
        }
        catch (OperationCanceledException)
        {
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-brief-notification-fallback-canceled-not-marked", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey,
                ["boardErrorClass"] = boardException.GetType().Name
            });
            StatusText.Text = "오늘 브리핑 알림이 취소되어 오늘 표시로 기록하지 않았습니다.";
        }
        catch (Exception fallbackException) when (fallbackException is not OperationCanceledException)
        {
            WindowsRuntimeDiagnostics.RecordUiEvent("daily-brief-notification-fallback-failed-not-marked", new Dictionary<string, string>
            {
                ["todayKey"] = plan.TodayKey,
                ["boardErrorClass"] = boardException.GetType().Name,
                ["fallbackErrorClass"] = fallbackException.GetType().Name
            });
            StatusText.Text = $"오늘 업무 보드와 알림을 열지 못해 오늘 표시로 기록하지 않았습니다: {fallbackException.GetType().Name}";
        }
    }

    private async Task ShowDailyBoardAsync(DateTimeOffset now, string dailyBoardTime, DailyBoardOpenOptions options)
    {
        _ = now;
        _ = dailyBoardTime;
        _mainFilter = options.Filter == BoardRouteFilter.Month ? BoardRouteFilter.Week : options.Filter;
        await RefreshTasksAsync();
        ShowShell(refresh: false);
        if (options.BringToFront)
        {
            BringWindowToFront(this);
        }

        StatusText.Text = options.ShowBriefSummary
            ? "오늘 업무를 열었습니다."
            : "업무 보드를 열었습니다.";
    }

    private static void BringWindowToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }

    private async Task ApproveReviewCandidateAsync(ReviewCandidate candidate)
    {
        try
        {
            var store = await GetStoreAsync();
            var task = await store.ResolveReviewCandidateAsTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
            await RefreshTasksAsync();
            if (_boardSnapshot is not null)
            {
                _reviewCandidatesWindow?.Refresh(_boardSnapshot.Candidates, _boardSnapshot.ClosureSuggestions, CanRetryLlmFailures, _boardSnapshot.ReviewCounts);
            }
            StatusText.Text = task is null
                ? "이미 처리된 항목입니다."
                : $"확인할 항목을 업무로 등록했습니다: {task.Title}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 등록 실패", ex);
        }
    }

    private async Task IgnoreReviewCandidateAsync(ReviewCandidate candidate)
    {
        try
        {
            var store = await GetStoreAsync();
            var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
            await RefreshReviewCandidatesAsync();
            if (_boardSnapshot is not null)
            {
                RenderTasks(_boardSnapshot);
            }
            StatusText.Text = ignored
                ? "확인할 항목을 무시했습니다."
                : "이미 처리된 항목입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 무시 실패", ex);
        }
    }

    private async Task SnoozeReviewCandidateAsync(ReviewCandidate candidate)
    {
        try
        {
            var store = await GetStoreAsync();
            var now = DateTimeOffset.UtcNow;
            var until = now.AddDays(1);
            var snoozed = await store.SnoozeReviewCandidateAsync(candidate.Id, until, now);
            await RefreshReviewCandidatesAsync();
            if (_boardSnapshot is not null)
            {
                RenderTasks(_boardSnapshot);
            }
            StatusText.Text = snoozed
                ? "확인할 항목은 내일까지 다시 표시하지 않습니다."
                : "이미 처리된 항목입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 나중에 보기 실패", ex);
        }
    }

    private async Task NotifyDueRemindersAsync()
    {
        if (_settings.ReminderLookAheadHours <= 0)
        {
            return;
        }

        if (ShouldSuppressPopupNotifications())
        {
            return;
        }

        var store = await GetStoreAsync();
        var tasks = await store.ListOpenTasksAsync();
        var now = DateTimeOffset.Now;
        var due = ReminderPlanner.DueForNotification(tasks, now, TimeSpan.FromHours(_settings.ReminderLookAheadHours));
        foreach (var reminder in due.Take(5))
        {
            if (IsDailyInterruptReminder(reminder)
                && !_notificationThrottle.ShouldNotifyOncePerDate(reminder.ReminderKey, now))
            {
                continue;
            }

            if (!_notificationThrottle.ShouldNotify(reminder.ReminderKey, now))
            {
                continue;
            }

            await _notificationSink.ShowAsync(new UserNotification(
                UserNotificationKind.Reminder,
                $"{reminder.DdayLabel} · {reminder.Title}",
                reminder.Reason,
                reminder.ReminderKey));
        }
    }

    private static bool IsDailyInterruptReminder(ReminderCandidate reminder) =>
        reminder.ReminderKey.EndsWith(":D-day", StringComparison.Ordinal)
        || reminder.ReminderKey.EndsWith(":snooze-due", StringComparison.Ordinal);

    private bool ShouldSuppressPopupNotifications() => IsVisible && IsActive;

    private async Task OpenTaskSourceAsync(LocalTaskItem task)
    {
        await OpenSourceMailAsync(task.SourceId);
    }

    private async Task<bool> ArchiveTaskAsync(LocalTaskItem task)
    {
        var store = await GetStoreAsync();
        var archived = await store.ArchiveTaskAsync(task.Id, DateTimeOffset.UtcNow);
        if (archived)
        {
            await RefreshTasksAsync();
        }

        StatusText.Text = archived
            ? "보관했습니다. 이 목록에는 다시 표시되지 않습니다."
            : "이미 처리된 항목입니다.";
        return archived;
    }

    private async Task OpenClosureSuggestionsAsync()
    {
        await OpenReviewCandidatesWindowAsync();
    }

    public async Task OpenClosureSuggestionsFromTrayAsync()
    {
        ShowShell(refresh: false);
        await OpenClosureSuggestionsAsync();
    }

    private async Task OpenWeeklyReviewAsync()
    {
        var store = await GetStoreAsync();
        var openTasks = await store.ListOpenTasksAsync();
        var archivedTasks = await store.ListArchivedTasksAsync(limit: 500);
        var candidates = await store.ListReviewCandidatesAsync();
        var suggestions = await store.ListWaitingClosureSuggestionsAsync();
        var summary = new WeeklyReviewPlanner().Build(openTasks, archivedTasks, candidates, suggestions, DateTimeOffset.Now);
        System.Windows.MessageBox.Show(summary.ToKoreanSummary(), "MailWhere 주간 리뷰", MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = "주간 리뷰를 열었습니다.";
    }

    public async Task OpenWeeklyReviewFromTrayAsync()
    {
        ShowShell(refresh: false);
        await OpenWeeklyReviewAsync();
    }

    private async Task<bool> SnoozeTaskAsync(LocalTaskItem task, DateTimeOffset until)
    {
        var store = await GetStoreAsync();
        var snoozed = await store.SnoozeTaskAsync(task.Id, until, DateTimeOffset.UtcNow);
        if (snoozed)
        {
            await RefreshTasksAsync();
        }

        StatusText.Text = snoozed
            ? $"{until:MM/dd HH:mm}까지 나중에 보기로 설정했습니다."
            : "이미 처리된 항목입니다.";
        return snoozed;
    }

    private async Task<bool> SetTaskDueAsync(LocalTaskItem task, DateTimeOffset dueAt)
    {
        var store = await GetStoreAsync();
        var updated = await store.UpdateTaskDueAtAsync(task.Id, dueAt, DateTimeOffset.UtcNow);
        if (updated)
        {
            await RefreshTasksAsync();
        }

        StatusText.Text = updated
            ? $"기한을 {dueAt:MM/dd}로 설정했습니다."
            : "이미 처리된 항목이라 기한을 바꾸지 못했습니다.";
        return updated;
    }

    private async Task<LocalTaskItem?> UpdateTaskDetailsAsync(LocalTaskItem task, TaskEditRequest edit)
    {
        var store = await GetStoreAsync();
        var updated = await store.UpdateTaskDetailsAsync(task.Id, edit, DateTimeOffset.UtcNow);
        if (updated is not null)
        {
            await RefreshTasksAsync();
        }

        StatusText.Text = updated is null
            ? "이미 처리된 항목이라 수정하지 못했습니다."
            : "업무 내용을 수정했습니다.";
        return updated;
    }

    private async Task EditTaskAsync(LocalTaskItem task)
    {
        var dialog = new TaskEditDialog(task)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.EditRequest is not { } edit)
        {
            return;
        }

        await UpdateTaskDetailsAsync(task, edit);
    }

    private async Task<LocalTaskItem?> CreateManualTaskAsync(string title, DateTimeOffset? dueAt)
    {
        try
        {
            var store = await GetStoreAsync();
            var created = await new ManualTaskService(store).CreateAsync(title, dueAt);
            await RefreshTasksAsync();
            StatusText.Text = "직접 추가한 할 일을 등록했습니다.";
            return created;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("직접 추가 실패", ex);
            return null;
        }
    }

    private void OpenMailSearchWindow()
    {
        if (_mailSearchWindow?.IsVisible == true)
        {
            BringWindowToFront(_mailSearchWindow);
            return;
        }

        _mailSearchWindow = new MailSearchWindow(GetDatabasePath)
        {
            Owner = IsVisible ? this : null
        };
        _mailSearchWindow.Closed += (_, _) => _mailSearchWindow = null;
        _mailSearchWindow.Show();
        BringWindowToFront(_mailSearchWindow);
        StatusText.Text = "메일 검색을 열었습니다.";
    }

    private async Task OpenReviewCandidatesWindowAsync()
    {
        var candidates = await RefreshReviewCandidatesAsync();
        var closureSuggestions = _boardSnapshot?.ClosureSuggestions ?? await RefreshClosureSuggestionsAsync();
        var reviewCounts = _boardSnapshot?.ReviewCounts ?? await CountReviewCandidateBacklogAsync(candidates.Count);
        if (_reviewCandidatesWindow?.IsVisible == true)
        {
            _reviewCandidatesWindow.Refresh(candidates, closureSuggestions, CanRetryLlmFailures, reviewCounts);
            BringWindowToFront(_reviewCandidatesWindow);
            return;
        }

        _reviewCandidatesWindow = new ReviewCandidatesWindow(
            candidates,
            closureSuggestions,
            ApproveReviewCandidateAsync,
            OpenReviewCandidateMailAsync,
            SnoozeReviewCandidateAsync,
            IgnoreReviewCandidateAsync,
            ResolveClosureSuggestionAsync,
            RetryLlmFailureReviewCandidatesAsync,
            CanRetryLlmFailures,
            reviewCounts)
        {
            Owner = IsVisible ? this : null
        };
        _reviewCandidatesWindow.Closed += (_, _) => _reviewCandidatesWindow = null;
        _reviewCandidatesWindow.Show();
        BringWindowToFront(_reviewCandidatesWindow);
        var total = candidates.Count + closureSuggestions.Count;
        StatusText.Text = total == 0
            ? "표시할 확인 필요 항목이 없습니다."
            : $"확인 필요 {total}개를 열었습니다.";
    }

    private async Task OpenArchiveWindowAsync()
    {
        var store = await GetStoreAsync();
        var archived = await store.ListArchivedTasksAsync(200);
        if (_archiveWindow?.IsVisible == true)
        {
            _archiveWindow.Refresh(archived);
            BringWindowToFront(_archiveWindow);
            return;
        }

        _archiveWindow = new ArchiveWindow(
            archived,
            OpenTaskSourceAsync,
            RestoreArchivedTaskAsync)
        {
            Owner = IsVisible ? this : null
        };
        _archiveWindow.Closed += (_, _) => _archiveWindow = null;
        _archiveWindow.Show();
        BringWindowToFront(_archiveWindow);
        StatusText.Text = archived.Count == 0
            ? "보관한 업무가 없습니다."
            : $"보관함 {archived.Count}개를 열었습니다.";
    }

    private async Task<bool> RestoreArchivedTaskAsync(LocalTaskItem task)
    {
        var store = await GetStoreAsync();
        var restored = await store.RestoreArchivedTaskAsync(task.Id, DateTimeOffset.UtcNow);
        if (restored)
        {
            await RefreshTasksAsync();
        }

        StatusText.Text = restored
            ? "업무 보드로 복원했습니다."
            : "이미 처리된 항목입니다.";
        return restored;
    }

    private async Task OpenReviewCandidateMailAsync(ReviewCandidate candidate)
    {
        await OpenSourceMailAsync(candidate.SourceId);
    }

    private async Task ResolveClosureSuggestionAsync(WaitingClosureSuggestion suggestion, bool archive)
    {
        var store = await GetStoreAsync();
        var resolution = archive ? WaitingClosureResolution.Archived : WaitingClosureResolution.Kept;
        var resolved = await store.ResolveWaitingClosureSuggestionAsync(suggestion.Id, resolution, DateTimeOffset.UtcNow);
        await RefreshTasksAsync();
        if (_boardSnapshot is not null)
        {
            _reviewCandidatesWindow?.Refresh(_boardSnapshot.Candidates, _boardSnapshot.ClosureSuggestions, CanRetryLlmFailures, _boardSnapshot.ReviewCounts);
        }

        StatusText.Text = resolved
            ? archive
                ? "대기 항목을 보관했습니다. Outlook 원본은 변경하지 않았습니다."
                : "대기 항목을 유지하고 제안만 닫았습니다."
            : "이미 처리된 보관 제안입니다.";
    }

    private async Task<ReviewCandidateRetrySummary> RetryLlmFailureReviewCandidatesAsync()
    {
        if (!CanRetryLlmFailures)
        {
            StatusText.Text = LlmAnalysisEnabled(_settings)
                ? "AI 분석 연결 테스트를 먼저 통과해야 실패 항목을 다시 시도할 수 있습니다."
                : "AI 분석 설정이 꺼져 있어 실패 항목을 다시 시도할 수 없습니다.";
            return new ReviewCandidateRetrySummary(0, 0, 0, 0, 0, 0, 0, 0);
        }

        if (_scanInProgress)
        {
            StatusText.Text = "메일 확인 중에는 AI 분석을 다시 시도할 수 없습니다.";
            return new ReviewCandidateRetrySummary(0, 0, 0, 0, 0, 0, 0, 0);
        }

        _scanInProgress = true;
        SetScanBusy(true, "AI 분석을 다시 시도하는 중입니다…");
        try
        {
            var store = await GetStoreAsync();
            var analyzer = BuildAnalyzer(_settings);
            var pipeline = new FollowUpPipeline(analyzer, store);
            var outlookSource = new OutlookComMailSource();
            var retry = new ReviewCandidateRetryService(
                store,
                pipeline,
                (candidate, cancellationToken) => outlookSource.TryReadBySourceIdAsync(candidate.SourceId, cancellationToken));

            var summary = await retry.RetryTransientLlmFailuresAsync();
            await RefreshTasksAsync();
            if (_boardSnapshot is not null)
            {
                _reviewCandidatesWindow?.Refresh(_boardSnapshot.Candidates, _boardSnapshot.ClosureSuggestions, CanRetryLlmFailures, _boardSnapshot.ReviewCounts);
            }
            StatusText.Text = ToRetryStatus(summary);
            return summary;
        }
        finally
        {
            _scanInProgress = false;
            SetScanBusy(false, "대기 중입니다.");
        }
    }

    private static string ToRetryStatus(ReviewCandidateRetrySummary summary)
    {
        if (summary.EligibleCount == 0)
        {
            return "다시 시도할 AI 분석 항목이 없습니다.";
        }

        return $"AI 분석 {summary.EligibleCount}개 중 {summary.RetriedCount}개 다시 시도 · 업무 {summary.TaskCreatedCount}개 · 확인 필요 {summary.ReviewCandidateCreatedCount}개 · 중복 {summary.DuplicateCount}개"
               + (summary.MissingSourceCount > 0 ? $" · 원본 없음 {summary.MissingSourceCount}개" : string.Empty)
               + (summary.SourceLookupFailureCount > 0 ? $" · 원본 조회 실패 {summary.SourceLookupFailureCount}개" : string.Empty);
    }

    private async Task OpenSettingsWindowAsync()
    {
        if (_settingsWindow?.IsVisible == true)
        {
            BringWindowToFront(_settingsWindow);
            return;
        }

        var window = new SettingsWindow(
            _settings,
            _settings.WindowsStartupRequested,
            new DeveloperToolActions(
                OpenFilterFromDeveloperAsync,
                ShowDeveloperToastAsync,
                ResetTodayBoardMarkerAsync,
                ResetLocalDataFromDeveloperAsync,
                AddSampleTasksAsync,
                AddSampleReviewCandidateAsync))
        {
            Owner = IsVisible ? this : null
        };
        _settingsWindow = window;
        window.Closed += (_, _) => _settingsWindow = null;
        var accepted = window.ShowDialog() == true;
        if (!accepted || window.UpdatedSettings is null)
        {
            return;
        }

        _settings = window.UpdatedSettings;
        WindowsRuntimeSettingsStore.Save(_settings);
        var startupRegistration = SyncStartupRegistration();
        if (_settings.AutomaticWatcherRequested && _settings.SmokeGatePassed)
        {
            StartOutlookEventWatcher();
            StartAutomaticScanTimer();
        }
        else
        {
            StopOutlookEventWatcher();
        }

        StatusText.Text = startupRegistration.Succeeded
            ? "설정을 저장했습니다."
            : ToStartupRegistrationStatus(startupRegistration);
    }

    private async Task OpenFilterFromDeveloperAsync(BoardRouteFilter filter)
    {
        _mainFilter = filter;
        if (_boardSnapshot is null)
        {
            await RefreshTasksAsync();
        }
        else
        {
            RenderTasks(_boardSnapshot);
        }
        ShowShell(refresh: false);
    }

    private async Task ShowDeveloperToastAsync()
    {
        await _notificationSink.ShowAsync(new UserNotification(
            UserNotificationKind.Reminder,
            "MailWhere 알림 테스트",
            "흰색 박스 중심의 간단한 알림으로 표시됩니다.",
            "developer-toast-test"));
        StatusText.Text = "알림 테스트를 보냈습니다.";
    }

    private async Task ResetTodayBoardMarkerAsync()
    {
        var store = await GetStoreAsync();
        await store.SetAppStateAsync(DailyBoardPlanner.LastShownDateKey, string.Empty);
        StatusText.Text = "오늘 업무 자동 표시 기록을 초기화했습니다.";
    }

    private async Task ResetLocalDataFromDeveloperAsync()
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "로컬 업무/확인 필요/처리 기록 DB를 삭제할까요?\n\n설정(runtime-settings.json)은 유지되고 Outlook 원본 메일은 변경하지 않습니다.",
            "로컬 업무 데이터 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            StatusText.Text = "로컬 업무 데이터 삭제를 취소했습니다.";
            throw new OperationCanceledException();
        }

        _store = null;
        var deleted = WindowsRuntimeDiagnostics.DeleteFollowUpDatabaseFiles();
        _boardSnapshot = new BoardSnapshot(
            Array.Empty<LocalTaskItem>(),
            Array.Empty<ReviewCandidate>(),
            new ReviewCandidateBacklogCounts(0, 0, 0),
            new Dictionary<Guid, ReplyProgressItem>(),
            Array.Empty<WaitingClosureSuggestion>());
        RenderTasks(_boardSnapshot);
        _reviewCandidatesWindow?.Refresh(Array.Empty<ReviewCandidate>(), Array.Empty<WaitingClosureSuggestion>(), CanRetryLlmFailures, new ReviewCandidateBacklogCounts(0, 0, 0));
        _archiveWindow?.Refresh(Array.Empty<LocalTaskItem>());
        StatusText.Text = deleted == 0
            ? "삭제할 로컬 업무 데이터가 없습니다. 설정은 유지됩니다."
            : $"로컬 업무 데이터 파일 {deleted}개를 삭제했습니다. 설정은 유지됩니다.";
        await Task.CompletedTask;
    }

    private async Task AddSampleTasksAsync()
    {
        var store = await GetStoreAsync();
        var now = DateTimeOffset.Now;
        var samples = new[]
        {
            BuildSampleTask("결제 플로우 문구 확인", now.Date.AddHours(15), "Design Partner", now.AddHours(-2)),
            BuildSampleTask("홈 QA 피드백 정리", now.Date.AddDays(1).AddHours(10), "Product Manager", now.AddHours(-5)),
            BuildSampleTask("운영 체크리스트 확인", null, "Customer Success", now.AddHours(-1))
        };
        foreach (var task in samples)
        {
            await store.SaveTaskAsync(task);
        }

        await RefreshTasksAsync();
        StatusText.Text = "샘플 업무 3개를 추가했습니다.";
    }

    private static LocalTaskItem BuildSampleTask(string title, DateTime? dueAt, string sender, DateTimeOffset receivedAt)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? due = dueAt is null
            ? null
            : new DateTimeOffset(dueAt.Value, TimeZoneInfo.Local.GetUtcOffset(dueAt.Value));
        return new LocalTaskItem(
            Guid.NewGuid(),
            title,
            due,
            StableHash.Create($"sample:{title}:{now:O}"),
            null,
            0.9,
            "개발자 도구 샘플 데이터",
            null,
            LocalTaskStatus.Open,
            null,
            now,
            now,
            SourceSenderDisplay: sender,
            SourceReceivedAt: receivedAt,
            Kind: FollowUpKind.ActionRequested);
    }

    private async Task AddSampleReviewCandidateAsync()
    {
        var store = await GetStoreAsync();
        var now = DateTimeOffset.Now;
        var mail = new EmailSnapshot(
            $"sample-review-{Guid.NewGuid():N}",
            now.AddHours(-3),
            "Finance Team",
            "비용 정산 안내",
            "이번 주 비용 정산 범위를 확인해주세요.");
        var candidate = ReviewCandidate.FromAnalysis(
            mail,
            new FollowUpAnalysis(
                FollowUpKind.ReviewNeeded,
                AnalysisDisposition.Review,
                0.52,
                "비용 정산 범위 확인",
                "개발자 도구 샘플 데이터",
                "비용 정산 범위 확인",
                new DateTimeOffset(now.Date.AddDays(2).AddHours(9), TimeZoneInfo.Local.GetUtcOffset(now.Date.AddDays(2)))),
            DateTimeOffset.UtcNow);
        await store.SaveReviewCandidateAsync(candidate);
        await RefreshReviewCandidatesAsync();
        StatusText.Text = "샘플 확인 필요 항목 1개를 추가했습니다.";
    }

    private bool MarkSmokeGatePassedAfterManualScan(MailScanSummary summary)
    {
        if (_settings.SmokeGatePassed)
        {
            return false;
        }

        if (summary.ReadCount <= 0 || summary.Warnings.Any(warning => warning.Severity == CapabilitySeverity.Blocked))
        {
            return false;
        }

        _settings = _settings with { SmokeGatePassed = true };
        WindowsRuntimeSettingsStore.Save(_settings);
        return true;
    }

    private bool CanRetryLlmFailures => LlmAnalysisEnabled(_settings) && _settings.HasCurrentLlmProbeProof();

    private static bool LlmAnalysisEnabled(RuntimeSettings settings) =>
        settings.ExternalLlmEnabled && settings.LlmProvider != LlmProviderKind.Disabled;

    private IFollowUpAnalyzer BuildAnalyzer(RuntimeSettings settings)
    {
        var rule = new RuleBasedFollowUpAnalyzer();
        if (!LlmAnalysisEnabled(settings) || !settings.HasCurrentLlmProbeProof())
        {
            return rule;
        }

        var client = LlmClientFactory.Create(settings.ToLlmEndpointSettings());
        return new LlmBackedFollowUpAnalyzer(client, rule, settings.LlmFallbackPolicy, settings.ToLlmAnalysisSettings());
    }

    private IWaitingClosureJudge BuildWaitingClosureJudge(RuntimeSettings settings)
    {
        var rule = new RuleBasedWaitingClosureJudge();
        if (!LlmAnalysisEnabled(settings) || !settings.HasCurrentLlmProbeProof())
        {
            return rule;
        }

        var client = LlmClientFactory.Create(settings.ToLlmEndpointSettings());
        return new LlmBackedWaitingClosureJudge(client, rule, settings.ToLlmAnalysisSettings());
    }

    private async Task<SqliteFollowUpStore> GetStoreAsync()
    {
        if (_store is not null)
        {
            return _store;
        }

        var directory = WindowsRuntimeDiagnostics.GetAppDataDirectory();
        Directory.CreateDirectory(directory);
        _store = new SqliteFollowUpStore(GetDatabasePath());
        await _store.InitializeAsync();
        return _store;
    }

    private static string GetDatabasePath() => Path.Combine(WindowsRuntimeDiagnostics.GetAppDataDirectory(), "followups.sqlite");

    private void OfferRuleFallbackAfterLlmFailure()
    {
        if (_fallbackPromptShownThisSession
            || !_settings.ShowLlmFailureFallbackPrompt
            || _settings.LlmFallbackPolicy != LlmFallbackPolicy.LlmOnly
            || !_settings.ExternalLlmEnabled
            || _settings.LlmProvider == LlmProviderKind.Disabled)
        {
            return;
        }

        _fallbackPromptShownThisSession = true;
        var result = System.Windows.MessageBox.Show(
            this,
            "LLM 연결 또는 분석이 실패했습니다.\n\n기본값은 실패한 메일을 확인 필요로 보관하고, LLM 연결이 복구되면 다시 분석하는 방식입니다.\n그래도 다음 메일 확인부터 규칙 기반 fallback을 허용할까요?\n\n나중에 설정 > AI 분석의 'AI 실패 시'에서 바꿀 수 있습니다.",
            "LLM 실패 처리",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _settings = _settings with { LlmFallbackPolicy = LlmFallbackPolicy.LlmThenRules };
        WindowsRuntimeSettingsStore.Save(_settings);
        StatusText.Text = "다음 메일 확인부터 LLM 실패 시 규칙 기반 fallback을 허용합니다.";
    }

    private void SetScanBusy(bool busy, string message)
    {
        ScanRecentMonthButton.IsEnabled = !busy;
        StopScanButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopScanButton.IsEnabled = busy;
        ScanProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressText.Text = message;
    }

    private void UpdateScanProgress(MailScanProgress progress)
    {
        ScanProgressText.Text = progress.Total is null
            ? progress.Message
            : $"{progress.Message} · {progress.Processed}/{progress.Total}";
    }

    private static DateTimeOffset? ParseManualDueAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var direct)
            ? direct
            : SimpleDueDateParser.TryParse(value, DateTimeOffset.Now);
    }

    private async Task ShowErrorAsync(string title, Exception ex)
    {
        StatusText.Text = $"{title}: {ex.GetType().Name}";
        await _notificationSink.ShowAsync(new UserNotification(UserNotificationKind.Error, title, ex.GetType().Name));
    }

    internal StartupRegistrationResult SyncStartupRegistration()
    {
        var result = WindowsStartupRegistration.ApplyRequestedState(_settings.WindowsStartupRequested);
        if (!result.Succeeded)
        {
            StatusText.Text = ToStartupRegistrationStatus(result);
        }

        return result;
    }

    private string ToStartupRegistrationStatus(StartupRegistrationResult result) =>
        _settings.WindowsStartupRequested
            ? $"시작 프로그램 등록에 실패했습니다: {result.FailureCode ?? "Unknown"}"
            : $"시작 프로그램 해제에 실패했습니다: {result.FailureCode ?? "Unknown"}";

    private static TaskListItem? GetTaskListItem(object sender) =>
        sender is FrameworkElement { Tag: TaskListItem item } ? item : null;

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private sealed record BoardSnapshot(
        IReadOnlyList<LocalTaskItem> Tasks,
        IReadOnlyList<ReviewCandidate> Candidates,
        ReviewCandidateBacklogCounts ReviewCounts,
        IReadOnlyDictionary<Guid, ReplyProgressItem> ReplyProgress,
        IReadOnlyList<WaitingClosureSuggestion> ClosureSuggestions);

    private sealed class TaskListItem
    {
        private TaskListItem(LocalTaskItem? task, string title, string dueText, string meta)
        {
            Task = task;
            Title = title;
            DueText = dueText;
            Meta = meta;
        }

        public LocalTaskItem? Task { get; }
        public string Title { get; }
        public string DueText { get; }
        public string Meta { get; }
        public bool HasTask => Task is not null;
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task?.SourceId);
        public Visibility DueButtonVisibility => Task is null ? Visibility.Collapsed : Visibility.Visible;

        public static TaskListItem FromTask(LocalTaskItem task, DateTimeOffset now, ReplyProgressItem? replyProgress = null)
        {
            var due = FollowUpPresentation.HumanDueText(task.DueAt, now);
            var sender = FollowUpPresentation.HumanSenderText(task.SourceSenderDisplay);
            var meta = $"{due} · {sender}";
            if (task.SourceReceivedAt is not null)
            {
                meta += $" · 메일: {FollowUpPresentation.HumanMailTime(task.SourceReceivedAt, now)}";
            }
            if (replyProgress is not null)
            {
                meta = $"{meta} · {replyProgress.SummaryText}";
            }

            return new TaskListItem(
                task,
                CompactLine(FollowUpPresentation.ActionTitle(task.Title), 120),
                due,
                meta);
        }

        public override string ToString() => Title;
    }
}
