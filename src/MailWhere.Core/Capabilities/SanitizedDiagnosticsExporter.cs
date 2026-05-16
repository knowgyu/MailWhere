using System.Text.Json;
using System.Text.RegularExpressions;

namespace MailWhere.Core.Capabilities;

public static class SanitizedDiagnosticsExporter
{
    private static readonly HashSet<string> AllowedDetailKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "count",
        "skippedCount",
        "version",
        "feature",
        "enabled",
        "mode",
        "errorClass",
        "statusCode"
    };

    private static readonly Regex NumericValue = new("^\\d{1,9}$", RegexOptions.Compiled);
    private static readonly Regex SafeCodeValue = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex SafeVersionValue = new("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$", RegexOptions.Compiled);

    public static string Export(CapabilityReport report)
    {
        var sanitized = report.Results.Select(ToSanitizedResult);

        return JsonSerializer.Serialize(new { report.CreatedAt, Results = sanitized }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Export(RuntimeGateSnapshot snapshot)
    {
        var sanitized = snapshot.CapabilityReport.Results.Select(ToSanitizedResult);
        var gate = new
        {
            snapshot.AutomaticWatcherGate.AutomaticWatcherEnabled,
            snapshot.AutomaticWatcherGate.Mode,
            ReasonCodes = snapshot.AutomaticWatcherGate.Reasons.Select(ToGateReasonCode).ToArray()
        };

        return JsonSerializer.Serialize(new
        {
            snapshot.CapabilityReport.CreatedAt,
            AutomaticWatcherGate = gate,
            Results = sanitized
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object ToSanitizedResult(CapabilityProbeResult result) => new
    {
        result.Id,
        Status = result.Status.ToString(),
        Severity = result.Severity.ToString(),
        SafeCode = ToSafeCode(result),
        Details = Sanitize(result.SanitizedDetails)
    };

    private static string ToSafeCode(CapabilityProbeResult result) =>
        result.Status switch
        {
            CapabilityStatus.Passed => "passed",
            CapabilityStatus.Warning => "warning",
            CapabilityStatus.Failed => result.Severity == CapabilitySeverity.Blocked ? "blocked" : "failed",
            CapabilityStatus.Skipped => "skipped",
            _ => "unknown"
        };

    private static IReadOnlyDictionary<string, string>? Sanitize(IReadOnlyDictionary<string, string>? details)
    {
        if (details is null)
        {
            return null;
        }

        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in details)
        {
            if (!AllowedDetailKeys.Contains(key) || !TrySanitizeValue(key, value, out var safeValue))
            {
                continue;
            }

            sanitized[key] = safeValue;
        }

        return sanitized;
    }

    private static bool TrySanitizeValue(string key, string? value, out string safeValue)
    {
        safeValue = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (key.Equals("count", StringComparison.OrdinalIgnoreCase)
            || key.Equals("skippedCount", StringComparison.OrdinalIgnoreCase))
        {
            if (!NumericValue.IsMatch(trimmed))
            {
                return false;
            }

            safeValue = trimmed;
            return true;
        }

        if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                safeValue = "true";
                return true;
            }

            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                safeValue = "false";
                return true;
            }

            return false;
        }

        if (key.Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            if (!SafeVersionValue.IsMatch(trimmed))
            {
                return false;
            }

            safeValue = trimmed;
            return true;
        }

        if (!SafeCodeValue.IsMatch(trimmed))
        {
            return false;
        }

        safeValue = trimmed;
        return true;
    }

    private static string ToGateReasonCode(string reason) =>
        reason switch
        {
            "Run one successful manual mail check before enabling automatic mail checks." => "manual-mail-check-required",
            "Outlook COM is unavailable." => "outlook-com-unavailable",
            "Outlook Inbox is not readable." => "outlook-inbox-unreadable",
            "Local storage is not writable." => "storage-not-writable",
            "LLM endpoint is unavailable and rule-only mode is not accepted." => "llm-unavailable-rule-only-not-accepted",
            "Mail body is not readable; manual selected-text mode should be used." => "mail-body-unreadable-manual-mode",
            "Automatic mail check is not requested in settings." => "automatic-check-not-requested",
            _ => "unspecified"
        };
}
