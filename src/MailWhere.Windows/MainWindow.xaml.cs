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
    private bool _scanInProgress;
    private DailyBoardWindow? _dailyBoardWindow;
    private AnalysisTelemetry _lastAnalysisTelemetry = AnalysisTelemetry.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _settings = WindowsRuntimeSettingsStore.Load();
        StartupToggle.IsChecked = IsStartupRegistered();
        ApplySettingsToControls(_settings);
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    public void SetNotificationSink(IUserNotificationSink notificationSink)
    {
        _notificationSink = notificationSink;
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
            StatusText.Text = "진단이 완료되었습니다. 민감한 메일 원문/제목/발신자는 진단 로그에 저장하지 않습니다.";
            await _notificationSink.ShowAsync(new UserNotification(UserNotificationKind.Diagnostics, "MailWhere 진단 완료", "진단 결과를 앱에서 확인하세요."));
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
            await ShowErrorAsync("최근 메일 스캔 실패", ex);
        }
    }

    private async void OpenDailyBoard_Click(object sender, RoutedEventArgs e)
    {
        await OpenDailyBoardAsync();
    }

    public async Task OpenDailyBoardAsync()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        await ShowDailyBoardAsync(DateTimeOffset.Now, _settings.DailyBoardTime);
    }

    public void OpenReviewTab()
    {
        Show();
        WindowState = WindowState.Normal;
        MainTabs.SelectedItem = ReviewTab;
        Activate();
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
                ScanRecentMonthButton.IsEnabled = true;
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
        SetScanBusy(true, "스캔 준비 중입니다…");
        try
        {
            if (refreshSettingsFromControls)
            {
                _settings = ReadSettingsFromControls();
                WindowsRuntimeSettingsStore.Save(_settings);
            }

            StatusText.Text = "최근 1개월 메일을 읽고 Action item을 분석하는 중입니다…";
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

            var summary = await scanner.ScanAsync(request, progress);
            _lastAnalysisTelemetry = analyzer is IAnalysisTelemetrySource telemetrySource
                ? telemetrySource.GetTelemetrySnapshot()
                : AnalysisTelemetry.Empty;
            var smokeGateRecorded = showSummaryNotification && MarkSmokeGatePassedAfterManualScan(summary);
            if (showSummaryNotification && smokeGateRecorded)
            {
                StatusText.Text = "수동 스캔이 성공해 자동 watcher smoke gate를 통과 처리했습니다.";
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
                + (smokeGateRecorded ? " · smoke gate 통과" : string.Empty);
            if (showSummaryNotification)
            {
                await _notificationSink.ShowAsync(new UserNotification(
                    UserNotificationKind.ScanSummary,
                    "최근 메일 스캔 완료",
                    $"할 일 {summary.TaskCreatedCount}건, 새 검토 후보 {newReviewCandidateCount}건을 찾았습니다. 검토는 MailWhere 보드에서 확인하세요.",
                    "scan-summary"));
            }

            return summary;
        }
        finally
        {
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
            "MailWhere 알림 테스트",
            "이런 식으로 마감 전 리마인드가 표시됩니다.",
            "notification-test"));
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
        foreach (var task in tasks)
        {
            var due = task.DueAt is null ? "마감 없음" : $"{DdayFormatter.Format(task.DueAt.Value, now)} · {task.DueAt.Value:MM/dd HH:mm}";
            TasksList.Items.Add($"{due}  |  {task.Title}  |  신뢰도 {task.Confidence:P0}");
        }

        if (tasks.Count == 0)
        {
            TasksList.Items.Add("아직 표시할 할 일이 없습니다. 최근 1개월 스캔을 실행해보세요.");
        }
    }

    private async Task<IReadOnlyList<ReviewCandidate>> RefreshReviewCandidatesAsync()
    {
        var store = await GetStoreAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        ReviewCandidatesList.Items.Clear();
        foreach (var candidate in candidates)
        {
            var due = candidate.Analysis.DueAt is null ? "마감 불명" : $"{DdayFormatter.Format(candidate.Analysis.DueAt.Value, DateTimeOffset.Now)} · {candidate.Analysis.DueAt.Value:MM/dd HH:mm}";
            ReviewCandidatesList.Items.Add(new ReviewCandidateListItem(
                candidate,
                $"{KoreanLabels.Kind(candidate.Analysis.Kind)} · {candidate.Analysis.Confidence:P0} · {due}  |  {candidate.Analysis.SuggestedTitle}  |  {candidate.Analysis.Reason}"));
        }

        if (candidates.Count == 0)
        {
            ReviewCandidatesList.Items.Add("검토 대기 후보가 없습니다.");
        }

        return candidates;
    }

    private async Task MaybeShowDailyBoardAsync()
    {
        var store = await GetStoreAsync();
        var now = DateTimeOffset.Now;
        var lastShownDate = await store.GetAppStateAsync(DailyBoardPlanner.LastShownDateKey);
        var plan = DailyBoardPlanner.Plan(now, _settings.DailyBoardTime, lastShownDate);
        if (!plan.ShouldShowNow)
        {
            return;
        }

        await ShowDailyBoardAsync(now, plan.DailyBoardTime);
        await store.SetAppStateAsync(DailyBoardPlanner.LastShownDateKey, plan.TodayKey);
    }

    private async Task ShowDailyBoardAsync(DateTimeOffset now, string dailyBoardTime)
    {
        if (_dailyBoardWindow?.IsVisible == true)
        {
            _dailyBoardWindow.Activate();
            return;
        }

        var store = await GetStoreAsync();
        var tasks = await store.ListOpenTasksAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        _dailyBoardWindow = new DailyBoardWindow(tasks, candidates, now, dailyBoardTime)
        {
            Owner = this
        };
        _dailyBoardWindow.Closed += (_, _) => _dailyBoardWindow = null;
        _dailyBoardWindow.Show();
        StatusText.Text = "오늘의 업무 보드를 표시했습니다.";
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
                ? "이미 처리된 확인 필요 후보입니다."
                : $"확인 필요 후보를 할 일로 등록했습니다: {task.Title}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 후보 등록 실패", ex);
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
                ? "확인 필요 후보를 무시 처리했습니다."
                : "이미 처리된 확인 필요 후보입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 후보 무시 실패", ex);
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
                ? "확인 필요 후보를 내일까지 숨겼습니다. 필요하면 다음 스캔/보드에서 다시 표시됩니다."
                : "이미 처리된 확인 필요 후보입니다.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("확인 필요 후보 나중에 보기 실패", ex);
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
        var provider = ParseProvider(((ComboBoxItem?)LlmProviderBox.SelectedItem)?.Tag?.ToString());
        var defaults = RuntimeSettings.ManagedSafeDefault;
        return RuntimeSettingsSerializer.Merge(new PartialRuntimeSettings(
            ManagedMode: true,
            ExternalLlmEnabled: LlmEnabledToggle.IsChecked == true,
            AutomaticWatcherRequested: AutoWatcherToggle.IsChecked == true,
            SmokeGatePassed: _settings.SmokeGatePassed,
            RuleOnlyModeAccepted: true,
            LlmProvider: provider,
            LlmEndpoint: LlmEndpointText.Text,
            LlmModel: LlmModelText.Text,
            LlmApiKeyEnvironmentVariable: LlmApiKeyEnvText.Text,
            LlmTimeoutSeconds: defaults.LlmTimeoutSeconds,
            LlmFallbackPolicy: ParseFallbackPolicy(((ComboBoxItem?)LlmFallbackPolicyBox.SelectedItem)?.Tag?.ToString()),
            RecentScanDays: ParseInt(ScanDaysText.Text, defaults.RecentScanDays),
            RecentScanMaxItems: ParseInt(MaxItemsText.Text, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: ParseInt(ReminderLookAheadText.Text, defaults.ReminderLookAheadHours),
            DailyBoardTime: DailyBoardTimeText.Text));
    }

    private void ApplySettingsToControls(RuntimeSettings settings)
    {
        LlmEnabledToggle.IsChecked = settings.ExternalLlmEnabled;
        AutoWatcherToggle.IsChecked = settings.AutomaticWatcherRequested;
        LlmEndpointText.Text = settings.LlmEndpoint;
        LlmModelText.Text = settings.LlmModel;
        LlmApiKeyEnvText.Text = settings.LlmApiKeyEnvironmentVariable ?? string.Empty;
        ScanDaysText.Text = settings.RecentScanDays.ToString();
        MaxItemsText.Text = settings.RecentScanMaxItems == 0 ? string.Empty : settings.RecentScanMaxItems.ToString();
        ReminderLookAheadText.Text = settings.ReminderLookAheadHours.ToString();
        DailyBoardTimeText.Text = settings.DailyBoardTime;
        LlmStatusText.Text = "LLM 연결 테스트 전입니다.";
        LlmProviderBox.SelectedItem = null;
        foreach (ComboBoxItem item in LlmProviderBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), settings.LlmProvider.ToString(), StringComparison.OrdinalIgnoreCase))
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
    }

    private static LlmProviderKind ParseProvider(string? value) =>
        Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var parsed) ? parsed : LlmProviderKind.Disabled;

    private static LlmFallbackPolicy ParseFallbackPolicy(string? value) =>
        Enum.TryParse<LlmFallbackPolicy>(value, ignoreCase: true, out var parsed) ? parsed : LlmFallbackPolicy.LlmThenRules;

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

        LlmFallbackPolicyBox.SelectedIndex = 1;
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private void SetScanBusy(bool busy, string message)
    {
        ScanRecentMonthButton.IsEnabled = !busy;
        TestLlmEndpointButton.IsEnabled = !busy;
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
}
