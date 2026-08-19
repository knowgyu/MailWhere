using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailWhere.Core.Analysis;
using MailWhere.Core.LLM;
using MailWhere.Core.Scheduling;

namespace MailWhere.Core.Capabilities;

public sealed record RuntimeSettings(
    bool ManagedMode,
    bool ExternalLlmEnabled,
    bool WindowsStartupRequested,
    bool AutomaticWatcherRequested,
    int AutomaticScanIntervalMinutes,
    bool SmokeGatePassed,
    bool RuleOnlyModeAccepted,
    LlmProviderKind LlmProvider,
    string LlmEndpoint,
    string LlmModel,
    string? LlmApiKey,
    string? LlmApiKeyEnvironmentVariable,
    int LlmTimeoutSeconds,
    LlmFallbackPolicy LlmFallbackPolicy,
    bool ShowLlmFailureFallbackPrompt,
    LlmThinkingControlMode LlmThinkingControlMode,
    LlmStructuredOutputMode LlmStructuredOutputMode,
    double LlmTemperature,
    int LlmMaxOutputTokens,
    int LlmBatchSize,
    LlmProbeProof? LastSuccessfulLlmProbeProof,
    int LlmInitialConcurrency,
    int LlmMaxConcurrency,
    int RecentScanDays,
    int RecentScanMaxItems,
    int ReminderLookAheadHours,
    string DailyBoardTime,
    int DailyBoardStartupDelayMinutes)
{
    public static RuntimeSettings ManagedSafeDefault { get; } = new(
        ManagedMode: true,
        ExternalLlmEnabled: false,
        WindowsStartupRequested: true,
        AutomaticWatcherRequested: false,
        AutomaticScanIntervalMinutes: 15,
        SmokeGatePassed: false,
        RuleOnlyModeAccepted: true,
        LlmProvider: LlmProviderKind.Disabled,
        LlmEndpoint: string.Empty,
        LlmModel: string.Empty,
        LlmApiKey: null,
        LlmApiKeyEnvironmentVariable: null,
        LlmTimeoutSeconds: 90,
        LlmFallbackPolicy: LlmFallbackPolicy.LlmOnly,
        ShowLlmFailureFallbackPrompt: false,
        LlmThinkingControlMode: LlmThinkingControlMode.Auto,
        LlmStructuredOutputMode: LlmStructuredOutputMode.JsonSchema,
        LlmTemperature: 0.1,
        LlmMaxOutputTokens: 0,
        LlmBatchSize: 4,
        LastSuccessfulLlmProbeProof: null,
        LlmInitialConcurrency: 1,
        LlmMaxConcurrency: 1,
        RecentScanDays: 30,
        RecentScanMaxItems: 0,
        ReminderLookAheadHours: 24,
        DailyBoardTime: DailyBoardPlanner.DefaultDailyBoardTime,
        DailyBoardStartupDelayMinutes: DailyBoardPlanner.DefaultStartupSettlingDelayMinutes);

    public LlmEndpointSettings ToLlmEndpointSettings() => new(
        LlmProvider,
        ExternalLlmEnabled,
        LlmEndpoint,
        LlmModel,
        ResolveApiKey(),
        LlmTimeoutSeconds);

    public LlmAnalysisSettings ToLlmAnalysisSettings() => new(
        ResolveAnalyzedThinkingControlMode(),
        LlmStructuredOutputMode,
        LlmTemperature,
        LlmMaxOutputTokens,
        LlmBatchSize);

    public LlmAnalysisSettings ToConfiguredLlmAnalysisSettings() => new(
        LlmThinkingControlMode,
        LlmStructuredOutputMode,
        LlmTemperature,
        LlmMaxOutputTokens,
        LlmBatchSize);

    private LlmThinkingControlMode ResolveAnalyzedThinkingControlMode() =>
        LlmThinkingControlMode == LlmThinkingControlMode.Auto && HasCurrentLlmProbeProof()
            ? LastSuccessfulLlmProbeProof!.SelectedThinkingControlMode
            : LlmThinkingControlMode;

    public string CurrentLlmProbeFingerprint() => LlmProbeProof.BuildFingerprint(
        LlmProvider,
        LlmEndpoint,
        LlmModel,
        LlmThinkingControlMode,
        LlmStructuredOutputMode,
        LlmTemperature,
        LlmMaxOutputTokens,
        LlmBatchSize);

    public bool HasCurrentLlmProbeProof() =>
        LastSuccessfulLlmProbeProof?.Fingerprint == CurrentLlmProbeFingerprint()
        && LastSuccessfulLlmProbeProof.SelectedThinkingControlMode is LlmThinkingControlMode.EnableThinkingFalse or LlmThinkingControlMode.ReasoningEffortNone
        && (LlmThinkingControlMode == LlmThinkingControlMode.Auto
            || LastSuccessfulLlmProbeProof.SelectedThinkingControlMode == LlmThinkingControlMode);

    public RuntimeSettings WithSuccessfulLlmProbeProof(DateTimeOffset probedAt, LlmThinkingControlMode selectedThinkingControlMode) =>
        this with { LastSuccessfulLlmProbeProof = LlmProbeProof.FromSettings(this, probedAt, selectedThinkingControlMode) };

    private string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(LlmApiKey))
        {
            return LlmApiKey;
        }

        if (string.IsNullOrWhiteSpace(LlmApiKeyEnvironmentVariable))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(LlmApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public sealed record LlmProbeProof(
    string Fingerprint,
    DateTimeOffset ProbedAt,
    string Provider,
    string Model,
    LlmThinkingControlMode SelectedThinkingControlMode)
{
    private const string RequestContractVersion = "qwen38-nonthinking-sampling-v1";

    public static LlmProbeProof FromSettings(RuntimeSettings settings, DateTimeOffset probedAt, LlmThinkingControlMode selectedThinkingControlMode) => new(
        settings.CurrentLlmProbeFingerprint(),
        probedAt,
        settings.LlmProvider.ToString(),
        settings.LlmModel,
        selectedThinkingControlMode);

    public static string BuildFingerprint(
        LlmProviderKind provider,
        string endpoint,
        string model,
        LlmThinkingControlMode thinkingControlMode,
        LlmStructuredOutputMode structuredOutputMode,
        double temperature,
        int maxOutputTokens,
        int batchSize)
    {
        var normalized = string.Join("\n", new[]
        {
            RequestContractVersion,
            provider.ToString(),
            NormalizeEndpoint(endpoint),
            model.Trim(),
            thinkingControlMode.ToString(),
            structuredOutputMode.ToString(),
            temperature.ToString("0.###", CultureInfo.InvariantCulture),
            maxOutputTokens.ToString(CultureInfo.InvariantCulture),
            batchSize.ToString(CultureInfo.InvariantCulture)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string NormalizeEndpoint(string endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim().TrimEnd('/').ToLowerInvariant();
}

public sealed record PartialRuntimeSettings(
    bool? ManagedMode = null,
    bool? ExternalLlmEnabled = null,
    bool? WindowsStartupRequested = null,
    bool? AutomaticWatcherRequested = null,
    int? AutomaticScanIntervalMinutes = null,
    bool? SmokeGatePassed = null,
    bool? RuleOnlyModeAccepted = null,
    LlmProviderKind? LlmProvider = null,
    string? LlmEndpoint = null,
    string? LlmModel = null,
    string? LlmApiKey = null,
    string? LlmApiKeyEnvironmentVariable = null,
    int? LlmTimeoutSeconds = null,
    LlmFallbackPolicy? LlmFallbackPolicy = null,
    bool? ShowLlmFailureFallbackPrompt = null,
    LlmThinkingControlMode? LlmThinkingControlMode = null,
    LlmStructuredOutputMode? LlmStructuredOutputMode = null,
    double? LlmTemperature = null,
    int? LlmMaxOutputTokens = null,
    int? LlmBatchSize = null,
    LlmProbeProof? LastSuccessfulLlmProbeProof = null,
    int? LlmInitialConcurrency = null,
    int? LlmMaxConcurrency = null,
    int? RecentScanDays = null,
    int? RecentScanMaxItems = null,
    int? ReminderLookAheadHours = null,
    string? DailyBoardTime = null,
    int? DailyBoardStartupDelayMinutes = null);

public static class RuntimeSettingsSerializer
{
    private const int MaxLlmConcurrency = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RuntimeSettings ParseOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RuntimeSettings.ManagedSafeDefault;
        }

        try
        {
            var partial = JsonSerializer.Deserialize<PartialRuntimeSettings>(json, JsonOptions);
            return Merge(partial);
        }
        catch
        {
            return RuntimeSettings.ManagedSafeDefault;
        }
    }

    public static string Serialize(RuntimeSettings settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    public static RuntimeSettings Merge(PartialRuntimeSettings? partial)
    {
        var defaults = RuntimeSettings.ManagedSafeDefault;
        var llmInitialConcurrency = Clamp(partial?.LlmInitialConcurrency, 1, MaxLlmConcurrency, defaults.LlmInitialConcurrency);
        var llmMaxConcurrency = Clamp(partial?.LlmMaxConcurrency, 1, MaxLlmConcurrency, defaults.LlmMaxConcurrency);
        var llmThinkingMode = partial?.LlmThinkingControlMode ?? defaults.LlmThinkingControlMode;
        var llmStructuredMode = partial?.LlmStructuredOutputMode ?? defaults.LlmStructuredOutputMode;
        var llmTemperature = ClampDouble(partial?.LlmTemperature, 0, 2, defaults.LlmTemperature);
        var llmMaxOutputTokens = Clamp(partial?.LlmMaxOutputTokens, 0, 8192, defaults.LlmMaxOutputTokens);
        var llmBatchSize = Clamp(partial?.LlmBatchSize, 1, 16, defaults.LlmBatchSize);
        if (partial?.LlmInitialConcurrency == 2 && partial?.LlmMaxConcurrency == 4)
        {
            // v0.5.0 serialized this pair as the default. Treat it as a
            // legacy default rather than an intentional opt-in so existing
            // installs move to the stable local-Ollama default automatically.
            llmInitialConcurrency = defaults.LlmInitialConcurrency;
            llmMaxConcurrency = defaults.LlmMaxConcurrency;
        }
        var merged = new RuntimeSettings(
            ManagedMode: partial?.ManagedMode ?? defaults.ManagedMode,
            ExternalLlmEnabled: partial?.ExternalLlmEnabled ?? defaults.ExternalLlmEnabled,
            WindowsStartupRequested: partial?.WindowsStartupRequested ?? defaults.WindowsStartupRequested,
            AutomaticWatcherRequested: partial?.AutomaticWatcherRequested ?? defaults.AutomaticWatcherRequested,
            AutomaticScanIntervalMinutes: Clamp(partial?.AutomaticScanIntervalMinutes, 1, 240, defaults.AutomaticScanIntervalMinutes),
            SmokeGatePassed: partial?.SmokeGatePassed ?? defaults.SmokeGatePassed,
            RuleOnlyModeAccepted: partial?.RuleOnlyModeAccepted ?? defaults.RuleOnlyModeAccepted,
            LlmProvider: partial?.LlmProvider ?? defaults.LlmProvider,
            LlmEndpoint: string.IsNullOrWhiteSpace(partial?.LlmEndpoint) ? defaults.LlmEndpoint : partial!.LlmEndpoint!.Trim(),
            LlmModel: string.IsNullOrWhiteSpace(partial?.LlmModel) ? defaults.LlmModel : partial!.LlmModel!,
            LlmApiKey: string.IsNullOrWhiteSpace(partial?.LlmApiKey) ? defaults.LlmApiKey : partial!.LlmApiKey,
            LlmApiKeyEnvironmentVariable: string.IsNullOrWhiteSpace(partial?.LlmApiKeyEnvironmentVariable) ? defaults.LlmApiKeyEnvironmentVariable : partial!.LlmApiKeyEnvironmentVariable,
            LlmTimeoutSeconds: Clamp(partial?.LlmTimeoutSeconds, 5, 180, defaults.LlmTimeoutSeconds),
            LlmFallbackPolicy: partial?.LlmFallbackPolicy ?? defaults.LlmFallbackPolicy,
            ShowLlmFailureFallbackPrompt: partial?.ShowLlmFailureFallbackPrompt ?? defaults.ShowLlmFailureFallbackPrompt,
            LlmThinkingControlMode: llmThinkingMode,
            LlmStructuredOutputMode: llmStructuredMode,
            LlmTemperature: llmTemperature,
            LlmMaxOutputTokens: llmMaxOutputTokens,
            LlmBatchSize: llmBatchSize,
            LastSuccessfulLlmProbeProof: partial?.LastSuccessfulLlmProbeProof,
            LlmInitialConcurrency: llmInitialConcurrency,
            LlmMaxConcurrency: llmMaxConcurrency,
            RecentScanDays: Clamp(partial?.RecentScanDays, 1, 90, defaults.RecentScanDays),
            RecentScanMaxItems: Clamp(partial?.RecentScanMaxItems, 0, 100000, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: Clamp(partial?.ReminderLookAheadHours, 0, 24 * 14, defaults.ReminderLookAheadHours),
            DailyBoardTime: DailyBoardPlanner.NormalizeDailyBoardTime(partial?.DailyBoardTime),
            DailyBoardStartupDelayMinutes: Clamp(partial?.DailyBoardStartupDelayMinutes, 0, 120, defaults.DailyBoardStartupDelayMinutes));

        return merged.HasCurrentLlmProbeProof()
            ? merged
            : merged with { LastSuccessfulLlmProbeProof = null };
    }

    private static int Clamp(int? value, int min, int max, int fallback) =>
        value is null ? fallback : Math.Clamp(value.Value, min, max);

    private static double ClampDouble(double? value, double min, double max, double fallback) =>
        value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? fallback
            : Math.Clamp(value.Value, min, max);
}

public sealed record RuntimeGateSnapshot(CapabilityReport CapabilityReport, GateResult AutomaticWatcherGate);

public static class RuntimeGateComposer
{
    public static RuntimeGateSnapshot Compose(RuntimeSettings settings, CapabilityReport report)
    {
        var input = new GateInput(
            ManagedMode: settings.ManagedMode,
            SmokeGatePassed: settings.SmokeGatePassed,
            OutlookComAvailable: report.Passed("outlook-com"),
            InboxReadable: report.Passed("outlook-inbox"),
            BodyReadable: report.Passed("outlook-mail-body"),
            StorageWritable: report.Passed("storage-writable"),
            LlmReachable: settings.ExternalLlmEnabled && report.Passed("llm-endpoint"),
            RuleOnlyModeAccepted: settings.RuleOnlyModeAccepted);

        var gate = FeatureGate.EvaluateAutomaticWatcher(input);
        if (!settings.AutomaticWatcherRequested)
        {
            gate = GateResult.Disabled(
                "manual",
                gate.Reasons
                    .Concat(new[] { "Automatic mail check is not requested in settings." })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }

        return new RuntimeGateSnapshot(report, gate);
    }
}
