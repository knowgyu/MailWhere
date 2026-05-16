using System.Windows;
using System.Windows.Controls;
using MailWhere.Core.Analysis;
using MailWhere.Core.Capabilities;
using MailWhere.Core.LLM;

namespace MailWhere.Windows;

public partial class SettingsWindow : Window
{
    private readonly RuntimeSettings _initialSettings;

    public SettingsWindow(RuntimeSettings settings, bool startupEnabled)
    {
        InitializeComponent();
        _initialSettings = settings;
        StartupEnabled = startupEnabled;
        Apply(settings, startupEnabled);
    }

    public RuntimeSettings? UpdatedSettings { get; private set; }
    public bool StartupEnabled { get; private set; }

    private void Apply(RuntimeSettings settings, bool startupEnabled)
    {
        StartupToggle.IsChecked = startupEnabled;
        AutoWatcherToggle.IsChecked = settings.AutomaticWatcherRequested;
        AutomaticScanIntervalText.Text = settings.AutomaticScanIntervalMinutes.ToString();
        ScanDaysText.Text = settings.RecentScanDays.ToString();
        MaxItemsText.Text = settings.RecentScanMaxItems == 0 ? string.Empty : settings.RecentScanMaxItems.ToString();
        ReminderLookAheadText.Text = settings.ReminderLookAheadHours.ToString();
        DailyBoardTimeText.Text = settings.DailyBoardTime;
        DailyBoardStartupDelayText.Text = settings.DailyBoardStartupDelayMinutes.ToString();
        LlmEnabledToggle.IsChecked = settings.ExternalLlmEnabled;
        SelectByTag(LlmProviderBox, (settings.LlmProvider == LlmProviderKind.Disabled ? LlmProviderKind.OllamaNative : settings.LlmProvider).ToString());
        LlmEndpointText.Text = settings.LlmEndpoint;
        LlmModelBox.Text = settings.LlmModel;
        LlmApiKeyBox.Password = settings.LlmApiKey ?? string.Empty;
        LlmApiKeyEnvText.Text = settings.LlmApiKeyEnvironmentVariable ?? string.Empty;
        SelectByTag(LlmAuthModeBox, !string.IsNullOrWhiteSpace(settings.LlmApiKey) ? "Direct" : !string.IsNullOrWhiteSpace(settings.LlmApiKeyEnvironmentVariable) ? "Environment" : "None");
        LlmTimeoutText.Text = settings.LlmTimeoutSeconds.ToString();
        SelectByTag(LlmFallbackPolicyBox, settings.LlmFallbackPolicy.ToString());
        UpdateAvailability();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdatedSettings = ReadSettings();
        StartupEnabled = StartupToggle.IsChecked == true;
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

        return RuntimeSettingsSerializer.Merge(new PartialRuntimeSettings(
            ManagedMode: true,
            ExternalLlmEnabled: llmEnabled,
            AutomaticWatcherRequested: AutoWatcherToggle.IsChecked == true,
            AutomaticScanIntervalMinutes: ParseInt(AutomaticScanIntervalText.Text, defaults.AutomaticScanIntervalMinutes),
            SmokeGatePassed: _initialSettings.SmokeGatePassed,
            RuleOnlyModeAccepted: true,
            LlmProvider: provider,
            LlmEndpoint: LlmEndpointText.Text,
            LlmModel: LlmModelBox.Text,
            LlmApiKey: apiKey,
            LlmApiKeyEnvironmentVariable: apiKeyEnv,
            LlmTimeoutSeconds: ParseInt(LlmTimeoutText.Text, defaults.LlmTimeoutSeconds),
            LlmFallbackPolicy: ParseFallbackPolicy(SelectedTag(LlmFallbackPolicyBox)),
            RecentScanDays: ParseInt(ScanDaysText.Text, defaults.RecentScanDays),
            RecentScanMaxItems: ParseInt(MaxItemsText.Text, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: ParseInt(ReminderLookAheadText.Text, defaults.ReminderLookAheadHours),
            DailyBoardTime: DailyBoardTimeText.Text,
            DailyBoardStartupDelayMinutes: ParseInt(DailyBoardStartupDelayText.Text, defaults.DailyBoardStartupDelayMinutes)));
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
            var current = LlmModelBox.Text.Trim();
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
        LlmTimeoutText.IsEnabled = llmEnabled;
        LlmFallbackPolicyBox.IsEnabled = llmEnabled;
        var authMode = SelectedTag(LlmAuthModeBox);
        LlmApiKeyBox.IsEnabled = llmEnabled && string.Equals(authMode, "Direct", StringComparison.OrdinalIgnoreCase);
        LlmApiKeyEnvText.IsEnabled = llmEnabled && string.Equals(authMode, "Environment", StringComparison.OrdinalIgnoreCase);
    }

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

        comboBox.SelectedIndex = 0;
    }
}
