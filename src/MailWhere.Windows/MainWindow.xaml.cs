using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
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
using MailWhere.Storage;
using MailWhere.OutlookCom;

namespace MailWhere.Windows;

public partial class MainWindow : Window
{
    private SqliteFollowUpStore? _store;
    private IUserNotificationSink _notificationSink = new NullNotificationSink();
    private readonly NotificationThrottle _notificationThrottle = new(TimeSpan.FromHours(1));
    private RuntimeSettings _settings;
    private DispatcherTimer? _reminderTimer;
    private DispatcherTimer? _dailyBoardTimer;
    private DispatcherTimer? _automaticScanTimer;
    private CancellationTokenSource? _scanCancellationSource;
    private readonly DateTimeOffset _appStartedAt = DateTimeOffset.Now;
    private bool _scanInProgress;
    private bool _fallbackPromptShownThisSession;
    private bool _backgroundStarted;
    private bool _allowExit;
    private DailyBoardWindow? _dailyBoardWindow;
    private AnalysisTelemetry _lastAnalysisTelemetry = AnalysisTelemetry.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _settings = UpgradeRuntimeSettings(WindowsRuntimeSettingsStore.Load());
        StartupToggle.IsChecked = IsStartupRegistered();
        ApplySettingsToControls(_settings);
        Loaded += async (_, _) => await StartBackgroundAsync();
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

    public void ShowShell()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ReportStatus(string message) => StatusText.Text = message;

    public void AllowExit() => _allowExit = true;

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        StatusText.Text = "창을 닫아도 트레이에서 계속 실행됩니다.";
    }

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettingsFromControls();
            var probe = new OutlookComCapabilityProbe();
            var outlookReport = probe.Run(includeBodyProbe: false);
            var results = outlookReport.Results
                .Concat(new[]
                {
                    WindowsRuntimeDiagnostics.ProbeStorageWritable(),
                    WindowsRuntimeDiagnostics.ProbeStartupToggleWritable(),
                    WindowsRuntimeDiagnostics.ProbeRuntimeSettings(settings),
                    ProbeLlmSettings(settings)
                })
                .ToArray();
            var report = new CapabilityReport(DateTimeOffset.UtcNow, results);
            var snapshot = RuntimeGateComposer.Compose(settings, report);
            DiagnosticsText.Text = SanitizedDiagnosticsExporter.Export(snapshot);
            StatusText.Text = "진단이 완료되었습니다. 설정의 문제 해결 영역에서 결과를 확인할 수 있습니다.";
            await _notificationSink.ShowAsync(new UserNotification(UserNotificationKind.Diagnostics, "MailWhere 진단 완료", "설정의 문제 해결 영역에서 결과를 확인하세요."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("진단 실패", ex);
        }
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
        StatusText.Text = "메일 확인 중지를 요청했습니다. 현재 LLM 요청이 정리되면 멈춥니다.";
        ScanProgressText.Text = "중지 요청됨…";
    }

    private async void OpenDailyBoard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await OpenDailyBoardAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"업무보드를 열지 못했습니다: {ex.GetType().Name}";
        }
    }

    public async Task OpenDailyBoardAsync()
    {
        await OpenDailyBoardAsync(DailyBoardOpenOptions.ManualAll());
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
        await ShowDailyBoardAsync(DateTimeOffset.Now, _settings.DailyBoardTime, options);
    }

    public void OpenReviewTab()
    {
        ShowShell();
        MainTabs.SelectedItem = ReviewTab;
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

    private async void EditTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetTaskListItem(sender) is { Task: { } task })
            {
                await EditTaskAsync(task);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"업무를 수정하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private async void TaskList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (TasksList.SelectedItem is TaskListItem { Task: { } task })
            {
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

    private async void TestLlmEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            return;
        }

        try
        {
            var settings = ReadSettingsFromControls();
            _settings = settings;
            WindowsRuntimeSettingsStore.Save(settings);
            LlmStatusText.Text = "LLM endpoint 연결을 테스트하는 중입니다…";
            TestLlmEndpointButton.IsEnabled = false;
            LoadLlmModelsButton.IsEnabled = false;
            ScanRecentMonthButton.IsEnabled = false;
            await Dispatcher.Yield(DispatcherPriority.Background);

            var result = await LlmEndpointProbe.ProbeAsync(settings.ToLlmEndpointSettings());
            LlmStatusText.Text = result.ToKoreanStatus();
            DiagnosticsText.Text = JsonSerializer.Serialize(new
            {
                llmProbe = new
                {
                    success = result.Success,
                    code = result.Code,
                    provider = result.Provider,
                    model = result.Model,
                    durationMs = Math.Round(result.Duration.TotalMilliseconds)
                }
            }, new JsonSerializerOptions { WriteIndented = true });
            StatusText.Text = result.Success
                ? "LLM 연결 테스트가 성공했습니다."
                : "LLM 연결 테스트가 실패했습니다. endpoint/model/provider를 확인하세요.";
            if (!result.Success && !string.Equals(result.Code, "not-configured", StringComparison.OrdinalIgnoreCase))
            {
                OfferRuleFallbackAfterLlmFailure();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("LLM 연결 테스트 실패", ex);
        }
        finally
        {
            if (!_scanInProgress)
            {
                TestLlmEndpointButton.IsEnabled = true;
                LoadLlmModelsButton.IsEnabled = true;
                ScanRecentMonthButton.IsEnabled = true;
                UpdateLlmControlAvailability();
            }
        }
    }

    private async void LoadLlmModels_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            return;
        }

        try
        {
            var settings = ReadSettingsFromControls();
            if (settings.LlmProvider == LlmProviderKind.Disabled || string.IsNullOrWhiteSpace(settings.LlmEndpoint))
            {
                StatusText.Text = "Provider와 endpoint를 먼저 입력하세요.";
                return;
            }

            LlmStatusText.Text = "모델 목록을 불러오는 중입니다…";
            LoadLlmModelsButton.IsEnabled = false;
            TestLlmEndpointButton.IsEnabled = false;
            await Dispatcher.Yield(DispatcherPriority.Background);

            var catalogSettings = settings.ToLlmEndpointSettings() with
            {
                Enabled = true,
                Model = string.IsNullOrWhiteSpace(settings.LlmModel) ? "catalog" : settings.LlmModel
            };
            var models = await LlmModelCatalog.FetchAsync(catalogSettings);
            ApplyModelList(models, settings.LlmModel);
            LlmStatusText.Text = models.Count == 0
                ? "모델 목록이 비어 있습니다. 모델명을 직접 입력하세요."
                : $"모델 {models.Count}개를 불러왔습니다.";
            StatusText.Text = models.Count == 0
                ? "모델을 직접 입력한 뒤 연결 테스트를 실행하세요."
                : "모델을 선택한 뒤 연결 테스트를 실행하세요.";
        }
        catch (Exception ex)
        {
            LlmStatusText.Text = $"모델 목록 불러오기 실패 · {ex.GetType().Name}";
            DiagnosticsText.Text = $"모델 목록 불러오기 실패\n{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            if (!_scanInProgress)
            {
                LoadLlmModelsButton.IsEnabled = true;
                TestLlmEndpointButton.IsEnabled = true;
                UpdateLlmControlAvailability();
            }
        }
    }

    private async void AddManualTask_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = await GetStoreAsync();
            var service = new ManualTaskService(store);
            var title = string.IsNullOrWhiteSpace(ManualTaskText.Text)
                ? $"수동 할 일 {DateTimeOffset.Now:MM/dd HH:mm}"
                : ManualTaskText.Text.Trim();
            var dueAt = ParseManualDueAt(ManualDueText.Text);
            await service.CreateAsync(title, dueAt);
            ManualTaskText.Clear();
            ManualDueText.Clear();
            await RefreshTasksAsync();
            StatusText.Text = "수동 할 일을 추가했습니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("수동 할 일 추가 실패", ex);
        }
    }

    private async Task OnLoadedAsync()
    {
        await RefreshTasksAsync();
        await RefreshReviewCandidatesAsync();
        await NotifyDueRemindersAsync();
        StartReminderTimer();

        if (_settings.AutomaticWatcherRequested && _settings.SmokeGatePassed)
        {
            await ScanRecentMailAsync(showSummaryNotification: false, refreshSettingsFromControls: false);
            StartAutomaticScanTimer();
        }

        StartDailyBoardTimer();
        await MaybeShowDailyBoardAsync();
    }

    private async Task<MailScanSummary> ScanRecentMailAsync(bool showSummaryNotification, bool refreshSettingsFromControls = true)
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
            if (refreshSettingsFromControls)
            {
                _settings = ReadSettingsFromControls();
                WindowsRuntimeSettingsStore.Save(_settings);
            }

            StatusText.Text = $"최근 {_settings.RecentScanDays}일 메일을 읽고 업무 후보를 분석하는 중입니다…";
            await Dispatcher.Yield(DispatcherPriority.Background);

            var store = await GetStoreAsync();
            var beforeCandidateIds = (await store.ListReviewCandidatesAsync())
                .Select(candidate => candidate.Id)
                .ToHashSet();
            var analyzer = BuildAnalyzer(_settings);
            var pipeline = new FollowUpPipeline(analyzer, store);
            var scanner = new MailActionScanner(new OutlookComMailSource(), pipeline);
            var now = DateTimeOffset.Now;
            var request = new MailScanRequest(
                _settings.RecentScanMaxItems,
                IncludeBody: true,
                now.AddDays(-_settings.RecentScanDays));
            var progress = new Progress<MailScanProgress>(UpdateScanProgress);

            var summary = await scanner.ScanAsync(request, progress, scanCancellationToken);
            _lastAnalysisTelemetry = analyzer is IAnalysisTelemetrySource telemetrySource
                ? telemetrySource.GetTelemetrySnapshot()
                : AnalysisTelemetry.Empty;
            var smokeGateRecorded = showSummaryNotification && MarkSmokeGatePassedAfterManualScan(summary);
            if (showSummaryNotification && smokeGateRecorded)
            {
                StatusText.Text = "수동 확인이 성공해 자동 메일 확인을 켤 수 있습니다.";
                if (_settings.AutomaticWatcherRequested)
                {
                    StartAutomaticScanTimer();
                }
            }

            await RefreshTasksAsync();
            var reviewCandidates = await RefreshReviewCandidatesAsync();
            if (!showSummaryNotification)
            {
                await NotifyDueRemindersAsync();
            }
            var newReviewCandidateCount = reviewCandidates.Count(candidate => !beforeCandidateIds.Contains(candidate.Id));

            var llmSummary = _lastAnalysisTelemetry.ToKoreanSummary();
            LlmStatusText.Text = llmSummary;
            StatusText.Text = $"최근 {_settings.RecentScanDays}일 메일 {summary.ReadCount}건 확인 · 할 일 {summary.TaskCreatedCount}건 · 새 검토 {newReviewCandidateCount}건 · 중복 {summary.DuplicateCount}건 · {llmSummary}"
                + (smokeGateRecorded ? " · 자동 확인 준비 완료" : string.Empty);
            if (showSummaryNotification)
            {
                await _notificationSink.ShowAsync(new UserNotification(
                    UserNotificationKind.ScanSummary,
                    "메일 확인 완료",
                    $"할 일 {summary.TaskCreatedCount}건, 새 검토 후보 {newReviewCandidateCount}건을 찾았습니다. 검토는 MailWhere 보드에서 확인하세요.",
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
            try
            {
                await NotifyDueRemindersAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"리마인더 점검 실패: {ex.GetType().Name}";
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
            try
            {
                await MaybeShowDailyBoardAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오늘의 업무 보드 점검 실패: {ex.GetType().Name}";
            }
        };
        _dailyBoardTimer.Start();
    }

    private void StartAutomaticScanTimer()
    {
        if (_automaticScanTimer is not null)
        {
            return;
        }

        _automaticScanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _automaticScanTimer.Tick += async (_, _) =>
        {
            if (!_settings.AutomaticWatcherRequested || !_settings.SmokeGatePassed)
            {
                return;
            }

            try
            {
                await ScanRecentMailAsync(showSummaryNotification: false, refreshSettingsFromControls: false);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"자동 메일 확인 실패: {ex.GetType().Name}";
            }
        };
        _automaticScanTimer.Start();
    }

    private async void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        await _notificationSink.ShowAsync(new UserNotification(
            UserNotificationKind.Reminder,
            "내일 마감 · 비용 자료 회신",
            "09:00까지 검토 후 회신이 필요합니다. 토스트 버튼으로 업무 보드를 바로 열 수 있습니다.",
            "notification-test"));
    }

    private async void ResetLocalData_Click(object sender, RoutedEventArgs e)
    {
        if (_scanInProgress)
        {
            StatusText.Text = "메일 확인 중에는 업무 데이터를 초기화할 수 없습니다. 확인을 중지하거나 끝난 뒤 다시 시도하세요.";
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
	            "저장된 할 일, 검토 후보, 메일 확인 중복 기록을 지웁니다.\n설정과 Windows 시작 등록은 유지합니다.",
            "MailWhere 업무 데이터 초기화",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _dailyBoardWindow?.Close();
            _dailyBoardWindow = null;
            _store = null;
            var deleted = WindowsRuntimeDiagnostics.DeleteFollowUpDatabaseFiles();
            await RefreshTasksAsync();
            await RefreshReviewCandidatesAsync();
            StatusText.Text = "업무 데이터를 초기화했습니다. 다음 메일 확인은 새 데이터베이스에서 시작합니다.";
            DiagnosticsText.Text = JsonSerializer.Serialize(new
            {
                localDataReset = new
                {
                    deletedFiles = deleted,
                    directory = WindowsRuntimeDiagnostics.GetAppDataDirectory(),
                    preserved = new[] { "runtime settings", "Windows startup registration" }
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("업무 데이터 초기화 실패", ex);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = ReadSettingsFromControls();
        WindowsRuntimeSettingsStore.Save(_settings);
        if (_settings.AutomaticWatcherRequested && _settings.SmokeGatePassed)
        {
            StartAutomaticScanTimer();
        }

        StatusText.Text = "설정을 저장했습니다.";
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        SetStartupRegistration(StartupToggle.IsChecked == true);
    }

    private void LlmEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        UpdateLlmControlAvailability();
    }

    private async void ApproveSelectedReview_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem item)
        {
            await ApproveReviewCandidateAsync(item.Candidate);
        }
    }

    private async void IgnoreSelectedReview_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem item)
        {
            await IgnoreReviewCandidateAsync(item.Candidate);
        }
    }

    private async void SnoozeSelectedReview_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem item)
        {
            await SnoozeReviewCandidateAsync(item.Candidate);
        }
    }

    private async void OpenSelectedReviewMail_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem item)
        {
            await OpenSourceMailAsync(item.Candidate.SourceId);
        }
    }

    private async void OpenSelectedReviewMail_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem item)
        {
            await OpenSourceMailAsync(item.Candidate.SourceId);
        }
    }

    private async void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt)
        {
            return;
        }

        if (e.Key == Key.A)
        {
            e.Handled = true;
            if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem approveItem)
            {
                await ApproveReviewCandidateAsync(approveItem.Candidate);
            }
        }
        else if (e.Key == Key.I)
        {
            e.Handled = true;
            if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem ignoreItem)
            {
                await IgnoreReviewCandidateAsync(ignoreItem.Candidate);
            }
        }
        else if (e.Key == Key.S)
        {
            e.Handled = true;
            if (ReviewCandidatesList.SelectedItem is ReviewCandidateListItem snoozeItem)
            {
                await SnoozeReviewCandidateAsync(snoozeItem.Candidate);
            }
        }
    }

    private async Task RefreshTasksAsync()
    {
        var store = await GetStoreAsync();
        var tasks = await store.ListOpenTasksAsync();
        TasksList.Items.Clear();
        var now = DateTimeOffset.Now;
        TasksList.Visibility = tasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TasksEmptyText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var task in tasks)
        {
            TasksList.Items.Add(TaskListItem.FromTask(task, now));
        }
    }

    private async Task<IReadOnlyList<ReviewCandidate>> RefreshReviewCandidatesAsync()
    {
        var store = await GetStoreAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        ReviewCandidatesList.Items.Clear();
        ReviewCandidatesList.Visibility = candidates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReviewCandidatesEmptyText.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var candidate in candidates)
        {
            var due = candidate.Analysis.DueAt is null ? "마감 불명" : $"{DdayFormatter.Format(candidate.Analysis.DueAt.Value, DateTimeOffset.Now)} · {candidate.Analysis.DueAt.Value:MM/dd HH:mm}";
            ReviewCandidatesList.Items.Add(new ReviewCandidateListItem(
                candidate,
                $"{KoreanLabels.Kind(candidate.Analysis.Kind)} · {due}\n{CompactLine(candidate.Analysis.SuggestedTitle, 54)}"));
        }

        return candidates;
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
        var store = await GetStoreAsync();
        var tasks = await store.ListOpenTasksAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        if (_dailyBoardWindow?.IsVisible == true)
        {
            _dailyBoardWindow.ApplyOpenOptions(options, now, dailyBoardTime, tasks, candidates);
            if (options.ShowBriefSummary)
            {
                WindowsRuntimeDiagnostics.RecordUiEvent("daily-board-existing-window-updated-today-brief", new Dictionary<string, string>
                {
                    ["origin"] = options.Origin.ToString()
                });
            }
            if (options.BringToFront)
            {
                BringDailyBoardToFront(_dailyBoardWindow);
            }

            StatusText.Text = options.ShowBriefSummary
                ? "업무 보드를 오늘 브리핑으로 갱신했습니다."
                : "업무 보드를 갱신했습니다.";
            return;
        }

        _dailyBoardWindow = new DailyBoardWindow(
            tasks,
            candidates,
            now,
            dailyBoardTime,
            options,
            OpenTaskSourceAsync,
            ArchiveTaskAsync,
            SnoozeTaskAsync,
            SetTaskDueAsync,
            UpdateTaskDetailsAsync,
            CreateManualTaskAsync,
            OpenReviewCandidatesFromBoardAsync);
        if (IsVisible)
        {
            _dailyBoardWindow.Owner = this;
        }
        _dailyBoardWindow.Closed += (_, _) => _dailyBoardWindow = null;
        _dailyBoardWindow.Show();
        if (options.BringToFront)
        {
            BringDailyBoardToFront(_dailyBoardWindow);
        }

        StatusText.Text = options.ShowBriefSummary
            ? "업무 보드를 오늘 브리핑으로 열었습니다."
            : "업무 보드를 열었습니다.";
    }

    private static void BringDailyBoardToFront(Window window)
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
            await RefreshReviewCandidatesAsync();
            StatusText.Text = task is null
                ? "이미 처리된 검토 후보입니다."
                : $"검토 후보를 할 일로 등록했습니다: {task.Title}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("검토 후보 등록 실패", ex);
        }
    }

    private async Task IgnoreReviewCandidateAsync(ReviewCandidate candidate)
    {
        try
        {
            var store = await GetStoreAsync();
            var ignored = await store.ResolveReviewCandidateAsNotTaskAsync(candidate.Id, DateTimeOffset.UtcNow);
            await RefreshTasksAsync();
            await RefreshReviewCandidatesAsync();
            StatusText.Text = ignored
                ? "검토 후보를 무시 처리했습니다."
                : "이미 처리된 검토 후보입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("검토 후보 무시 실패", ex);
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
            StatusText.Text = snoozed
                ? "검토 후보는 내일까지 다시 표시하지 않습니다."
                : "이미 처리된 검토 후보입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("검토 후보 나중에 보기 실패", ex);
        }
    }

    private async Task NotifyDueRemindersAsync()
    {
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

    private Task OpenReviewCandidatesFromBoardAsync()
    {
        OpenReviewTab();
        return Task.CompletedTask;
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

    private IFollowUpAnalyzer BuildAnalyzer(RuntimeSettings settings)
    {
        var rule = new RuleBasedFollowUpAnalyzer();
        if (!settings.ExternalLlmEnabled || settings.LlmProvider == LlmProviderKind.Disabled)
        {
            return rule;
        }

        var client = LlmClientFactory.Create(settings.ToLlmEndpointSettings());
        return new LlmBackedFollowUpAnalyzer(client, rule, settings.LlmFallbackPolicy);
    }

    private static CapabilityProbeResult ProbeLlmSettings(RuntimeSettings settings)
    {
        var enabled = settings.ExternalLlmEnabled && settings.LlmProvider != LlmProviderKind.Disabled;
        return enabled
            ? CapabilityProbeResult.Warning("llm-endpoint", "LlmConfiguredNotPinged", new Dictionary<string, string>
            {
                ["feature"] = "llm-endpoint",
                ["enabled"] = "true",
                ["mode"] = settings.LlmProvider.ToString()
            })
            : CapabilityProbeResult.Warning("llm-endpoint", "EndpointNotConfigured", new Dictionary<string, string>
            {
                ["feature"] = "llm-endpoint",
                ["enabled"] = "false"
            });
    }

    private async Task<SqliteFollowUpStore> GetStoreAsync()
    {
        if (_store is not null)
        {
            return _store;
        }

        var directory = WindowsRuntimeDiagnostics.GetAppDataDirectory();
        Directory.CreateDirectory(directory);
        _store = new SqliteFollowUpStore(Path.Combine(directory, "followups.sqlite"));
        await _store.InitializeAsync();
        return _store;
    }

    private RuntimeSettings ReadSettingsFromControls()
    {
        var llmEnabled = LlmEnabledToggle.IsChecked == true;
        var provider = llmEnabled
            ? ParseVisibleProvider(((ComboBoxItem?)LlmProviderBox.SelectedItem)?.Tag?.ToString())
            : LlmProviderKind.Disabled;
        var defaults = RuntimeSettings.ManagedSafeDefault;
        return RuntimeSettingsSerializer.Merge(new PartialRuntimeSettings(
            ManagedMode: true,
            ExternalLlmEnabled: llmEnabled,
            AutomaticWatcherRequested: AutoWatcherToggle.IsChecked == true,
            SmokeGatePassed: _settings.SmokeGatePassed,
            RuleOnlyModeAccepted: true,
            LlmProvider: provider,
            LlmEndpoint: LlmEndpointText.Text,
            LlmModel: LlmModelBox.Text,
            LlmApiKeyEnvironmentVariable: LlmApiKeyEnvText.Text,
            LlmTimeoutSeconds: ParseInt(LlmTimeoutText.Text, defaults.LlmTimeoutSeconds),
            LlmFallbackPolicy: ParseFallbackPolicy(((ComboBoxItem?)LlmFallbackPolicyBox.SelectedItem)?.Tag?.ToString()),
            RecentScanDays: ParseInt(ScanDaysText.Text, defaults.RecentScanDays),
            RecentScanMaxItems: ParseInt(MaxItemsText.Text, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: ParseInt(ReminderLookAheadText.Text, defaults.ReminderLookAheadHours),
            DailyBoardTime: DailyBoardTimeText.Text,
            DailyBoardStartupDelayMinutes: ParseInt(DailyBoardStartupDelayText.Text, defaults.DailyBoardStartupDelayMinutes)));
    }

    private void ApplySettingsToControls(RuntimeSettings settings)
    {
        LlmEnabledToggle.IsChecked = settings.ExternalLlmEnabled;
        AutoWatcherToggle.IsChecked = settings.AutomaticWatcherRequested;
        LlmEndpointText.Text = settings.LlmEndpoint;
        ApplyModelList(Array.Empty<string>(), settings.LlmModel);
        LlmApiKeyEnvText.Text = settings.LlmApiKeyEnvironmentVariable ?? string.Empty;
        ScanDaysText.Text = settings.RecentScanDays.ToString();
        MaxItemsText.Text = settings.RecentScanMaxItems == 0 ? string.Empty : settings.RecentScanMaxItems.ToString();
        ReminderLookAheadText.Text = settings.ReminderLookAheadHours.ToString();
        DailyBoardTimeText.Text = settings.DailyBoardTime;
        DailyBoardStartupDelayText.Text = settings.DailyBoardStartupDelayMinutes.ToString();
        LlmTimeoutText.Text = settings.LlmTimeoutSeconds.ToString();
        LlmStatusText.Text = "LLM 연결 테스트 전입니다.";
        var visibleProvider = settings.LlmProvider == LlmProviderKind.Disabled
            ? LlmProviderKind.OllamaNative
            : settings.LlmProvider;
        LlmProviderBox.SelectedItem = null;
        foreach (ComboBoxItem item in LlmProviderBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), visibleProvider.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LlmProviderBox.SelectedItem = item;
                break;
            }
        }

        if (LlmProviderBox.SelectedItem is null)
        {
            LlmProviderBox.SelectedIndex = 0;
        }

        ApplyFallbackPolicyToControls(settings.LlmFallbackPolicy);
        UpdateLlmControlAvailability();
    }

    private static LlmProviderKind ParseVisibleProvider(string? value) =>
        Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var parsed) && parsed != LlmProviderKind.Disabled
            ? parsed
            : LlmProviderKind.OllamaNative;

    private static LlmFallbackPolicy ParseFallbackPolicy(string? value) =>
        Enum.TryParse<LlmFallbackPolicy>(value, ignoreCase: true, out var parsed) ? parsed : LlmFallbackPolicy.LlmOnly;

    private void ApplyFallbackPolicyToControls(LlmFallbackPolicy fallbackPolicy)
    {
        LlmFallbackPolicyBox.SelectedItem = null;
        foreach (ComboBoxItem item in LlmFallbackPolicyBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), fallbackPolicy.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LlmFallbackPolicyBox.SelectedItem = item;
                return;
            }
        }

        LlmFallbackPolicyBox.SelectedIndex = 0;
    }

    private void ApplyModelList(IReadOnlyList<string> models, string currentModel)
    {
        var selected = string.IsNullOrWhiteSpace(currentModel) ? string.Empty : currentModel.Trim();
        LlmModelBox.Items.Clear();
        foreach (var model in models)
        {
            LlmModelBox.Items.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(selected)
            && !models.Any(model => string.Equals(model, selected, StringComparison.OrdinalIgnoreCase)))
        {
            LlmModelBox.Items.Add(selected);
        }

        LlmModelBox.Text = selected;
    }

    private void OfferRuleFallbackAfterLlmFailure()
    {
        if (_fallbackPromptShownThisSession
            || _settings.LlmFallbackPolicy != LlmFallbackPolicy.LlmOnly
            || !_settings.ExternalLlmEnabled
            || _settings.LlmProvider == LlmProviderKind.Disabled)
        {
            return;
        }

        _fallbackPromptShownThisSession = true;
        var result = System.Windows.MessageBox.Show(
            this,
            "LLM 연결 또는 분석이 실패했습니다.\n\n기본값은 실패한 메일을 검토 후보로 보관하고, LLM 연결이 복구되면 다시 분석하는 방식입니다.\n그래도 다음 메일 확인부터 규칙 기반 fallback을 허용할까요?\n\n나중에 고급 설정의 'LLM 분석 실패 처리'에서 바꿀 수 있습니다.",
            "LLM 실패 처리",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _settings = _settings with { LlmFallbackPolicy = LlmFallbackPolicy.LlmThenRules };
        WindowsRuntimeSettingsStore.Save(_settings);
        ApplyFallbackPolicyToControls(_settings.LlmFallbackPolicy);
        StatusText.Text = "다음 메일 확인부터 LLM 실패 시 규칙 기반 fallback을 허용합니다.";
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private void SetScanBusy(bool busy, string message)
    {
        ScanRecentMonthButton.IsEnabled = !busy;
        StopScanButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopScanButton.IsEnabled = busy;
        ScanProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressText.Text = message;
        UpdateLlmControlAvailability();
    }

    private void UpdateLlmControlAvailability()
    {
        var enabled = !_scanInProgress && LlmEnabledToggle.IsChecked == true;
        LlmProviderBox.IsEnabled = enabled;
        LlmEndpointText.IsEnabled = enabled;
        LlmModelBox.IsEnabled = enabled;
        LlmTimeoutText.IsEnabled = !_scanInProgress && LlmEnabledToggle.IsChecked == true;
        TestLlmEndpointButton.IsEnabled = enabled;
        LoadLlmModelsButton.IsEnabled = enabled;
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
        DiagnosticsText.Text = $"{title}\n{ex.GetType().Name}: {ex.Message}";
        await _notificationSink.ShowAsync(new UserNotification(UserNotificationKind.Error, title, ex.GetType().Name));
    }

    private static void SetStartupRegistration(bool enabled)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var command = BuildStartupCommand(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(command))
            {
                key.SetValue("MailWhere", command);
            }
        }
        else
        {
            key.DeleteValue("MailWhere", throwOnMissingValue: false);
        }
    }

    internal static string? BuildStartupCommand(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        return $"\"{exePath.Trim().Trim('\"')}\"";
    }

    private static bool IsStartupRegistered()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        var configured = key?.GetValue("MailWhere") as string;
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return string.Equals(configured.Trim().Trim('"'), currentPath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ReviewCandidateListItem(ReviewCandidate Candidate, string Display)
    {
        public override string ToString() => Display;
    }

    private static TaskListItem? GetTaskListItem(object sender) =>
        sender is FrameworkElement { Tag: TaskListItem item } ? item : null;

    private sealed class TaskListItem
    {
        private TaskListItem(LocalTaskItem? task, string title, string meta)
        {
            Task = task;
            Title = title;
            Meta = meta;
        }

        public LocalTaskItem? Task { get; }
        public string Title { get; }
        public string Meta { get; }
        public bool HasTask => Task is not null;
        public bool CanOpen => !string.IsNullOrWhiteSpace(Task?.SourceId);
        public string DueButtonText => Task?.DueAt is null ? "기한 설정" : "기한 변경";
        public Visibility DueButtonVisibility => Task is null ? Visibility.Collapsed : Visibility.Visible;

        public static TaskListItem FromTask(LocalTaskItem task, DateTimeOffset now)
        {
            var due = task.DueAt is null ? "기한 미정" : DdayFormatter.Format(task.DueAt.Value, now);
            var sender = string.IsNullOrWhiteSpace(task.SourceSenderDisplay) ? "직접 추가" : CompactLine(task.SourceSenderDisplay, 18);
            var received = task.SourceReceivedAt ?? task.CreatedAt;
            return new TaskListItem(task, CompactLine(task.Title, 120), $"{due} · {sender} · {received:MM/dd HH:mm}");
        }

        public override string ToString() => Title;
    }
}
