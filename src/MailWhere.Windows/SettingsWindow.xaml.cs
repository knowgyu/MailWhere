using System.Windows;
using System.Windows.Controls;
using MailWhere.Core.Analysis;
using MailWhere.Core.Capabilities;
using MailWhere.Core.LLM;
using MailWhere.Core.Scheduling;

namespace MailWhere.Windows;

public sealed record DeveloperToolActions(
    Func<BoardRouteFilter, Task> OpenFilterAsync,
    Func<Task> ShowToastAsync,
    Func<Task> ResetTodayMarkerAsync,
    Func<Task> ResetLocalDataAsync,
    Func<Task> AddSampleTasksAsync,
    Func<Task> AddSampleReviewAsync);

public partial class SettingsWindow : Window
{
    private readonly RuntimeSettings _initialSettings;
    private readonly DeveloperToolActions _developerToolActions;

    public SettingsWindow(RuntimeSettings settings, bool startupEnabled, DeveloperToolActions developerToolActions)
    {
        InitializeComponent();
        _initialSettings = settings;
        _developerToolActions = developerToolActions;
        PopulateChoiceBoxes();
        Apply(settings, startupEnabled);
    }

    public RuntimeSettings? UpdatedSettings { get; private set; }

    private void PopulateChoiceBoxes()
    {
        RecentRangeBox.Items.Clear();
        foreach (var choice in RecentMailRangeChoices.All)
        {
            RecentRangeBox.Items.Add(new ComboBoxItem { Tag = choice.Days.ToString(), Content = ToRecentRangeLabel(choice.Days) });
        }

        ReminderModeBox.Items.Clear();
        foreach (var choice in ReminderNotificationChoices.All)
        {
            ReminderModeBox.Items.Add(new ComboBoxItem { Tag = choice.Mode.ToString(), Content = ToReminderModeLabel(choice.Mode) });
        }
    }

    private void Apply(RuntimeSettings settings, bool startupEnabled)
    {
        StartupToggle.IsChecked = startupEnabled;
        AutoWatcherToggle.IsChecked = settings.AutomaticWatcherRequested;
        SelectByTag(RecentRangeBox, RecentMailRangeChoices.NormalizeDays(settings.RecentScanDays).ToString());
        SelectByTag(AutomaticScanIntervalBox, NormalizeInterval(settings.AutomaticScanIntervalMinutes).ToString());
        SelectByTag(ReminderModeBox, ReminderNotificationChoices.FromLookAheadHours(settings.ReminderLookAheadHours).ToString());
        LlmEnabledToggle.IsChecked = settings.ExternalLlmEnabled;
        SelectByTag(LlmProviderBox, (settings.LlmProvider == LlmProviderKind.Disabled ? LlmProviderKind.OllamaNative : settings.LlmProvider).ToString());
        LlmEndpointText.Text = settings.LlmEndpoint;
        LlmModelBox.Text = settings.LlmModel;
        LlmApiKeyBox.Password = settings.LlmApiKey ?? string.Empty;
        LlmApiKeyEnvText.Text = settings.LlmApiKeyEnvironmentVariable ?? string.Empty;
        SelectByTag(LlmAuthModeBox, !string.IsNullOrWhiteSpace(settings.LlmApiKey) ? "Direct" : !string.IsNullOrWhiteSpace(settings.LlmApiKeyEnvironmentVariable) ? "Environment" : "None");
        SelectByTag(LlmTimeoutBox, NormalizeTimeout(settings.LlmTimeoutSeconds).ToString());
        SelectByTag(LlmFallbackPolicyBox, settings.LlmFallbackPolicy.ToString());
        LlmFailureFallbackPromptToggle.IsChecked = settings.ShowLlmFailureFallbackPrompt;
        UpdateAvailability();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdatedSettings = ReadSettings();
        DialogResult = true;
    }

    private RuntimeSettings ReadSettings()
    {
        var defaults = RuntimeSettings.ManagedSafeDefault;
        var llmEnabled = LlmEnabledToggle.IsChecked == true;
        var provider = llmEnabled ? ParseProvider(SelectedTag(LlmProviderBox)) : LlmProviderKind.Disabled;
        var authMode = SelectedTag(LlmAuthModeBox);
        var apiKey = string.Equals(authMode, "Direct", StringComparison.OrdinalIgnoreCase) ? NullIfBlank(LlmApiKeyBox.Password) : null;
        var apiKeyEnv = string.Equals(authMode, "Environment", StringComparison.OrdinalIgnoreCase) ? NullIfBlank(LlmApiKeyEnvText.Text) : null;
        var reminderMode = Enum.TryParse<ReminderNotificationMode>(SelectedTag(ReminderModeBox), ignoreCase: true, out var parsedMode)
            ? parsedMode
            : ReminderNotificationChoices.DefaultMode;

        return RuntimeSettingsSerializer.Merge(new PartialRuntimeSettings(
            ManagedMode: true,
            ExternalLlmEnabled: llmEnabled,
            WindowsStartupRequested: StartupToggle.IsChecked == true,
            AutomaticWatcherRequested: AutoWatcherToggle.IsChecked == true,
            AutomaticScanIntervalMinutes: ParseInt(SelectedTag(AutomaticScanIntervalBox), defaults.AutomaticScanIntervalMinutes),
            SmokeGatePassed: _initialSettings.SmokeGatePassed,
            RuleOnlyModeAccepted: true,
            LlmProvider: provider,
            LlmEndpoint: LlmEndpointText.Text,
            LlmModel: LlmModelBox.Text,
            LlmApiKey: apiKey,
            LlmApiKeyEnvironmentVariable: apiKeyEnv,
            LlmTimeoutSeconds: ParseInt(SelectedTag(LlmTimeoutBox), defaults.LlmTimeoutSeconds),
            LlmFallbackPolicy: ParseFallbackPolicy(SelectedTag(LlmFallbackPolicyBox)),
            ShowLlmFailureFallbackPrompt: LlmFailureFallbackPromptToggle.IsChecked == true,
            LlmInitialConcurrency: _initialSettings.LlmInitialConcurrency,
            LlmMaxConcurrency: _initialSettings.LlmMaxConcurrency,
            RecentScanDays: ParseInt(SelectedTag(RecentRangeBox), defaults.RecentScanDays),
            RecentScanMaxItems: _initialSettings.RecentScanMaxItems,
            ReminderLookAheadHours: ReminderNotificationChoices.ToLookAheadHours(reminderMode),
            DailyBoardTime: string.IsNullOrWhiteSpace(_initialSettings.DailyBoardTime) ? defaults.DailyBoardTime : _initialSettings.DailyBoardTime,
            DailyBoardStartupDelayMinutes: _initialSettings.DailyBoardStartupDelayMinutes));
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            LlmStatusText.Text = "연결 테스트 중입니다…";
            TestConnectionButton.IsEnabled = false;
            LoadModelsButton.IsEnabled = false;
            var result = await LlmEndpointProbe.ProbeAsync(settings.ToLlmEndpointSettings());
            LlmStatusText.Text = result.ToKoreanStatus();
        }
        catch (Exception ex)
        {
            LlmStatusText.Text = $"연결 테스트 실패 · {ex.GetType().Name}";
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            LoadModelsButton.IsEnabled = true;
            UpdateAvailability();
        }
    }

    private async void LoadModels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            if (settings.LlmProvider == LlmProviderKind.Disabled || string.IsNullOrWhiteSpace(settings.LlmEndpoint))
            {
                LlmStatusText.Text = "주소를 먼저 입력하세요.";
                return;
            }

            LlmStatusText.Text = "모델 목록을 불러오는 중입니다…";
            LoadModelsButton.IsEnabled = false;
            TestConnectionButton.IsEnabled = false;
            var catalogSettings = settings.ToLlmEndpointSettings() with
            {
                Enabled = true,
                Model = string.IsNullOrWhiteSpace(settings.LlmModel) ? "catalog" : settings.LlmModel
            };
            var models = await LlmModelCatalog.FetchAsync(catalogSettings);
            ApplyModelList(models, settings.LlmModel);
            LlmStatusText.Text = models.Count == 0 ? "모델명을 직접 입력하세요." : $"모델 {models.Count}개를 불러왔습니다.";
        }
        catch (Exception ex)
        {
            LlmStatusText.Text = $"모델 불러오기 실패 · {ex.GetType().Name}";
        }
        finally
        {
            LoadModelsButton.IsEnabled = true;
            TestConnectionButton.IsEnabled = true;
            UpdateAvailability();
        }
    }

    private void ApplyModelList(IReadOnlyList<string> models, string currentModel)
    {
        var current = currentModel.Trim();
        LlmModelBox.Items.Clear();
        foreach (var model in models)
        {
            LlmModelBox.Items.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(current) && !models.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            LlmModelBox.Items.Add(current);
        }

        LlmModelBox.Text = current;
    }

    private void LlmEnabled_Click(object sender, RoutedEventArgs e) => UpdateAvailability();
    private void AuthMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAvailability();

    private void UpdateAvailability()
    {
        var llmEnabled = LlmEnabledToggle.IsChecked == true;
        LlmProviderBox.IsEnabled = llmEnabled;
        LlmEndpointText.IsEnabled = llmEnabled;
        LlmModelBox.IsEnabled = llmEnabled;
        LoadModelsButton.IsEnabled = llmEnabled;
        TestConnectionButton.IsEnabled = llmEnabled;
        LlmAuthModeBox.IsEnabled = llmEnabled;
        LlmTimeoutBox.IsEnabled = llmEnabled;
        LlmFallbackPolicyBox.IsEnabled = llmEnabled;
        LlmFailureFallbackPromptToggle.IsEnabled = llmEnabled;
        var authMode = SelectedTag(LlmAuthModeBox);
        LlmApiKeyBox.IsEnabled = llmEnabled && string.Equals(authMode, "Direct", StringComparison.OrdinalIgnoreCase);
        LlmApiKeyEnvText.IsEnabled = llmEnabled && string.Equals(authMode, "Environment", StringComparison.OrdinalIgnoreCase);
    }

    private async void OpenToday_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(() => _developerToolActions.OpenFilterAsync(BoardRouteFilter.Today), "오늘 화면을 열었습니다.");
    private async void OpenWeek_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(() => _developerToolActions.OpenFilterAsync(BoardRouteFilter.Week), "이번 주 화면을 열었습니다.");
    private async void OpenNoDue_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(() => _developerToolActions.OpenFilterAsync(BoardRouteFilter.NoDue), "날짜 없음 화면을 열었습니다.");
    private async void OpenAll_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(() => _developerToolActions.OpenFilterAsync(BoardRouteFilter.All), "전체 화면을 열었습니다.");
    private async void Toast_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(_developerToolActions.ShowToastAsync, "알림 테스트를 보냈습니다.");
    private async void SampleTasks_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(_developerToolActions.AddSampleTasksAsync, "샘플 업무를 추가했습니다.");
    private async void SampleReview_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(_developerToolActions.AddSampleReviewAsync, "샘플 확인 필요 항목을 추가했습니다.");
    private async void ResetTodayMarker_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(_developerToolActions.ResetTodayMarkerAsync, "오늘 표시 기록을 초기화했습니다.");
    private async void ResetLocalData_Click(object sender, RoutedEventArgs e) => await RunDeveloperActionAsync(_developerToolActions.ResetLocalDataAsync, "로컬 업무 데이터를 삭제했습니다. 설정은 유지됩니다.");

    private async Task RunDeveloperActionAsync(Func<Task> action, string successMessage)
    {
        try
        {
            await action();
            DeveloperStatusText.Text = successMessage;
        }
        catch (OperationCanceledException)
        {
            DeveloperStatusText.Text = "취소했습니다.";
        }
        catch (Exception ex)
        {
            DeveloperStatusText.Text = $"처리하지 못했습니다: {ex.GetType().Name}";
        }
    }

    private static int NormalizeInterval(int value) => value switch
    {
        <= 1 => 1,
        <= 10 => 10,
        <= 15 => 15,
        <= 30 => 30,
        _ => 60
    };

    private static int NormalizeTimeout(int value) => value switch
    {
        <= 30 => 30,
        <= 60 => 60,
        <= 90 => 90,
        _ => 180
    };

    private static string ToRecentRangeLabel(int days) => days switch
    {
        1 => "최근 1일",
        7 => "최근 7일",
        30 => "최근 30일",
        90 => "최근 90일",
        _ => $"최근 {days}일"
    };

    private static string ToReminderModeLabel(ReminderNotificationMode mode) => mode switch
    {
        ReminderNotificationMode.Off => "끄기",
        ReminderNotificationMode.DueToday => "당일만",
        ReminderNotificationMode.DayBefore => "하루 전부터",
        _ => mode.ToString()
    };

    private static LlmProviderKind ParseProvider(string? value) =>
        Enum.TryParse<LlmProviderKind>(value, ignoreCase: true, out var parsed) && parsed != LlmProviderKind.Disabled
            ? parsed
            : LlmProviderKind.OllamaNative;

    private static LlmFallbackPolicy ParseFallbackPolicy(string? value) =>
        Enum.TryParse<LlmFallbackPolicy>(value, ignoreCase: true, out var parsed) ? parsed : LlmFallbackPolicy.LlmOnly;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(System.Windows.Controls.ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }
}
