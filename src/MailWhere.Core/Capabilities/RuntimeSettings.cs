using System.Text.Json;
using System.Text.Json.Serialization;
using MailWhere.Core.Analysis;
using MailWhere.Core.LLM;
using MailWhere.Core.Scheduling;

namespace MailWhere.Core.Capabilities;

public sealed record RuntimeSettings(
    bool ManagedMode,
    bool ExternalLlmEnabled,
    bool AutomaticWatcherRequested,
    bool SmokeGatePassed,
    bool RuleOnlyModeAccepted,
    LlmProviderKind LlmProvider,
    string LlmEndpoint,
    string LlmModel,
    string? LlmApiKey,
    string? LlmApiKeyEnvironmentVariable,
    int LlmTimeoutSeconds,
    LlmFallbackPolicy LlmFallbackPolicy,
    int RecentScanDays,
    int RecentScanMaxItems,
    int ReminderLookAheadHours,
    string DailyBoardTime,
    int DailyBoardStartupDelayMinutes)
{
    public static RuntimeSettings ManagedSafeDefault { get; } = new(
        ManagedMode: true,
        ExternalLlmEnabled: false,
        AutomaticWatcherRequested: false,
        SmokeGatePassed: false,
        RuleOnlyModeAccepted: true,
        LlmProvider: LlmProviderKind.Disabled,
        LlmEndpoint: "http://localhost:11434",
        LlmModel: string.Empty,
        LlmApiKey: null,
        LlmApiKeyEnvironmentVariable: null,
        LlmTimeoutSeconds: 90,
        LlmFallbackPolicy: LlmFallbackPolicy.LlmOnly,
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

public sealed record PartialRuntimeSettings(
    bool? ManagedMode = null,
    bool? ExternalLlmEnabled = null,
    bool? AutomaticWatcherRequested = null,
    bool? SmokeGatePassed = null,
    bool? RuleOnlyModeAccepted = null,
    LlmProviderKind? LlmProvider = null,
    string? LlmEndpoint = null,
    string? LlmModel = null,
    string? LlmApiKey = null,
    string? LlmApiKeyEnvironmentVariable = null,
    int? LlmTimeoutSeconds = null,
    LlmFallbackPolicy? LlmFallbackPolicy = null,
    int? RecentScanDays = null,
    int? RecentScanMaxItems = null,
    int? ReminderLookAheadHours = null,
    string? DailyBoardTime = null,
    int? DailyBoardStartupDelayMinutes = null);

public static class RuntimeSettingsSerializer
{
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
        return new RuntimeSettings(
            ManagedMode: partial?.ManagedMode ?? defaults.ManagedMode,
            ExternalLlmEnabled: partial?.ExternalLlmEnabled ?? defaults.ExternalLlmEnabled,
            AutomaticWatcherRequested: partial?.AutomaticWatcherRequested ?? defaults.AutomaticWatcherRequested,
            SmokeGatePassed: partial?.SmokeGatePassed ?? defaults.SmokeGatePassed,
            RuleOnlyModeAccepted: partial?.RuleOnlyModeAccepted ?? defaults.RuleOnlyModeAccepted,
            LlmProvider: partial?.LlmProvider ?? defaults.LlmProvider,
            LlmEndpoint: string.IsNullOrWhiteSpace(partial?.LlmEndpoint) ? defaults.LlmEndpoint : partial!.LlmEndpoint!,
            LlmModel: string.IsNullOrWhiteSpace(partial?.LlmModel) ? defaults.LlmModel : partial!.LlmModel!,
            LlmApiKey: string.IsNullOrWhiteSpace(partial?.LlmApiKey) ? defaults.LlmApiKey : partial!.LlmApiKey,
            LlmApiKeyEnvironmentVariable: string.IsNullOrWhiteSpace(partial?.LlmApiKeyEnvironmentVariable) ? defaults.LlmApiKeyEnvironmentVariable : partial!.LlmApiKeyEnvironmentVariable,
            LlmTimeoutSeconds: Clamp(partial?.LlmTimeoutSeconds, 5, 180, defaults.LlmTimeoutSeconds),
            LlmFallbackPolicy: partial?.LlmFallbackPolicy ?? defaults.LlmFallbackPolicy,
            RecentScanDays: Clamp(partial?.RecentScanDays, 1, 31, defaults.RecentScanDays),
            RecentScanMaxItems: Clamp(partial?.RecentScanMaxItems, 0, 100000, defaults.RecentScanMaxItems),
            ReminderLookAheadHours: Clamp(partial?.ReminderLookAheadHours, 1, 24 * 14, defaults.ReminderLookAheadHours),
            DailyBoardTime: DailyBoardPlanner.NormalizeDailyBoardTime(partial?.DailyBoardTime),
            DailyBoardStartupDelayMinutes: Clamp(partial?.DailyBoardStartupDelayMinutes, 0, 120, defaults.DailyBoardStartupDelayMinutes));
    }

    private static int Clamp(int? value, int min, int max, int fallback) =>
        value is null ? fallback : Math.Clamp(value.Value, min, max);
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
                    .Concat(new[] { "Automatic watcher is not requested in settings." })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
        }

        return new RuntimeGateSnapshot(report, gate);
    }
}
