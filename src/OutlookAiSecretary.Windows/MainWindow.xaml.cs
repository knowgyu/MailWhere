using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using OutlookAiSecretary.Core.Analysis;
using OutlookAiSecretary.Core.Capabilities;
using OutlookAiSecretary.Core.LLM;
using OutlookAiSecretary.Core.Localization;
using OutlookAiSecretary.Core.Notifications;
using OutlookAiSecretary.Core.Pipeline;
using OutlookAiSecretary.Core.Reminders;
using OutlookAiSecretary.Core.Scanning;
using OutlookAiSecretary.Storage;
using OutlookAiSecretary.OutlookCom;

namespace OutlookAiSecretary.Windows;

public partial class MainWindow : Window
{
    private SqliteFollowUpStore? _store;
    private IUserNotificationSink _notificationSink = new NullNotificationSink();
    private readonly NotificationThrottle _notificationThrottle = new(TimeSpan.FromHours(1));
    private RuntimeSettings _settings;
    private DispatcherTimer? _reminderTimer;

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
            await _notificationSink.ShowAsync(new UserNotification(UserNotificationKind.Diagnostics, "메일 비서 진단 완료", "진단 결과를 앱에서 확인하세요."));
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
            await ScanRecentMailAsync(showSummaryNotification: false);
        }
    }

    private async Task<MailScanSummary> ScanRecentMailAsync(bool showSummaryNotification)
    {
        _settings = ReadSettingsFromControls();
        WindowsRuntimeSettingsStore.Save(_settings);
        StatusText.Text = "최근 메일을 읽고 Action item을 분석하는 중입니다…";

        var store = await GetStoreAsync();
        var analyzer = BuildAnalyzer(_settings);
        var pipeline = new FollowUpPipeline(analyzer, store);
        var scanner = new MailActionScanner(new OutlookComMailSource(), pipeline);
        var now = DateTimeOffset.Now;
        var request = new MailScanRequest(
            _settings.RecentScanMaxItems,
            IncludeBody: true,
            now.AddDays(-_settings.RecentScanDays));

        var summary = await scanner.ScanAsync(request);
        var smokeGateRecorded = showSummaryNotification && MarkSmokeGatePassedAfterManualScan(summary);
        if (showSummaryNotification && smokeGateRecorded)
        {
            StatusText.Text = "수동 스캔이 성공해 자동 watcher smoke gate를 통과 처리했습니다.";
        }

        await RefreshTasksAsync();
        await RefreshReviewCandidatesAsync();
        await NotifyDueRemindersAsync();

        StatusText.Text = $"최근 {_settings.RecentScanDays}일 메일 {summary.ReadCount}건 확인 · 할 일 {summary.TaskCreatedCount}건 · 검토 후보 {summary.ReviewCandidateCount}건 · 중복 {summary.DuplicateCount}건"
            + (smokeGateRecorded ? " · smoke gate 통과" : string.Empty);
        if (showSummaryNotification)
        {
            await _notificationSink.ShowAsync(new UserNotification(
                UserNotificationKind.ScanSummary,
                "최근 메일 스캔 완료",
                $"할 일 {summary.TaskCreatedCount}건, 검토 후보 {summary.ReviewCandidateCount}건을 찾았습니다.",
                "scan-summary"));
        }

        return summary;
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

    private async void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        await _notificationSink.ShowAsync(new UserNotification(
            UserNotificationKind.Reminder,
            "메일 비서 알림 테스트",
            "이런 식으로 마감 전 리마인드가 표시됩니다.",
            "notification-test"));
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = ReadSettingsFromControls();
        WindowsRuntimeSettingsStore.Save(_settings);
        StatusText.Text = "설정을 저장했습니다.";
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        SetStartupRegistration(StartupToggle.IsChecked == true);
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

    private async Task RefreshReviewCandidatesAsync()
    {
        var store = await GetStoreAsync();
        var candidates = await store.ListReviewCandidatesAsync();
        ReviewCandidatesList.Items.Clear();
        foreach (var candidate in candidates)
        {
            var due = candidate.Analysis.DueAt is null ? "마감 불명" : $"{DdayFormatter.Format(candidate.Analysis.DueAt.Value, DateTimeOffset.Now)} · {candidate.Analysis.DueAt.Value:MM/dd HH:mm}";
            ReviewCandidatesList.Items.Add($"{KoreanLabels.Kind(candidate.Analysis.Kind)} · {candidate.Analysis.Confidence:P0} · {due}  |  {candidate.Analysis.SuggestedTitle}  |  {candidate.Analysis.Reason}");
        }

        if (candidates.Count == 0)
        {
            ReviewCandidatesList.Items.Add("검토 대기 후보가 없습니다.");
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
        return new LlmBackedFollowUpAnalyzer(client, rule);
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
            RecentScanDays: ParseInt(ScanDaysText.Text, defaults.RecentScanDays),
            RecentScanMaxItems: ParseInt(MaxItemsText.Text, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: ParseInt(ReminderLookAheadText.Text, defaults.ReminderLookAheadHours)));
    }

    private void ApplySettingsToControls(RuntimeSettings settings)
    {
        LlmEnabledToggle.IsChecked = settings.ExternalLlmEnabled;
        AutoWatcherToggle.IsChecked = settings.AutomaticWatcherRequested;
        LlmEndpointText.Text = settings.LlmEndpoint;
        LlmModelText.Text = settings.LlmModel;
        LlmApiKeyEnvText.Text = settings.LlmApiKeyEnvironmentVariable ?? string.Empty;
        ScanDaysText.Text = settings.RecentScanDays.ToString();
        MaxItemsText.Text = settings.RecentScanMaxItems.ToString();
        ReminderLookAheadText.Text = settings.ReminderLookAheadHours.ToString();
        foreach (ComboBoxItem item in LlmProviderBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), settings.LlmProvider.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                LlmProviderBox.SelectedItem = item;
                return;
            }
        }

        LlmProviderBox.SelectedIndex = 0;
    }

    private static LlmProviderKind ParseProvider(string? value) =>
        Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var parsed) ? parsed : LlmProviderKind.Disabled;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

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
                key.SetValue("OutlookAiSecretary", command);
            }
        }
        else
        {
            key.DeleteValue("OutlookAiSecretary", throwOnMissingValue: false);
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
        var configured = key?.GetValue("OutlookAiSecretary") as string;
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return string.Equals(configured.Trim().Trim('"'), currentPath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }
}
