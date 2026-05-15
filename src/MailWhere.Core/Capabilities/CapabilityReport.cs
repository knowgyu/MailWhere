namespace MailWhere.Core.Capabilities;

public enum CapabilityStatus
{
    Passed,
    Warning,
    Failed,
    Skipped
}

public enum CapabilitySeverity
{
    Info,
    Degraded,
    Blocked
}

public sealed record CapabilityProbeResult(
    string Id,
    CapabilityStatus Status,
    CapabilitySeverity Severity,
    string Message,
    IReadOnlyDictionary<string, string>? SanitizedDetails = null)
{
    public static CapabilityProbeResult Passed(string id, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(id, CapabilityStatus.Passed, CapabilitySeverity.Info, message, details);

    public static CapabilityProbeResult Failed(string id, string message, CapabilitySeverity severity = CapabilitySeverity.Degraded, IReadOnlyDictionary<string, string>? details = null) =>
        new(id, CapabilityStatus.Failed, severity, message, details);

    public static CapabilityProbeResult Warning(string id, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(id, CapabilityStatus.Warning, CapabilitySeverity.Degraded, message, details);
}

public sealed record CapabilityReport(DateTimeOffset CreatedAt, IReadOnlyList<CapabilityProbeResult> Results)
{
    public bool Passed(string id) => Results.Any(result => result.Id == id && result.Status == CapabilityStatus.Passed);
    public bool HasFailure(string id) => Results.Any(result => result.Id == id && result.Status == CapabilityStatus.Failed);
}
