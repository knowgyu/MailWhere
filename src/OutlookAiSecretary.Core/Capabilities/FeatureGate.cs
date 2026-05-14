namespace OutlookAiSecretary.Core.Capabilities;

public sealed record GateInput(
    bool ManagedMode,
    bool SmokeGatePassed,
    bool OutlookComAvailable,
    bool InboxReadable,
    bool BodyReadable,
    bool StorageWritable,
    bool LlmReachable,
    bool RuleOnlyModeAccepted);

public sealed record GateResult(
    bool AutomaticWatcherEnabled,
    string Mode,
    IReadOnlyList<string> Reasons)
{
    public static GateResult Enabled() => new(true, "normal", Array.Empty<string>());
    public static GateResult Disabled(string mode, params string[] reasons) => new(false, mode, reasons);
}

public static class FeatureGate
{
    public static GateResult EvaluateAutomaticWatcher(GateInput input)
    {
        var reasons = new List<string>();

        if (!input.SmokeGatePassed)
        {
            reasons.Add("Phase 0 smoke gate must pass before automatic watching.");
        }

        if (!input.OutlookComAvailable)
        {
            reasons.Add("Outlook COM is unavailable.");
        }

        if (!input.InboxReadable)
        {
            reasons.Add("Outlook Inbox is not readable.");
        }

        if (!input.StorageWritable)
        {
            reasons.Add("Local storage is not writable.");
        }

        if (!input.LlmReachable && !input.RuleOnlyModeAccepted)
        {
            reasons.Add("LLM endpoint is unavailable and rule-only mode is not accepted.");
        }

        if (reasons.Count > 0)
        {
            return GateResult.Disabled("degraded", reasons.ToArray());
        }

        return input.BodyReadable
            ? GateResult.Enabled()
            : GateResult.Disabled("metadata-only", "Mail body is not readable; manual selected-text mode should be used.");
    }
}
