namespace MailWhere.Core.Scanning;

public sealed record AutomaticScanWindowPlan(DateTimeOffset Since, bool UsedLastSuccessfulScan);

public static class AutomaticScanWindowPlanner
{
    public const string LastSuccessfulScanStateKey = "mail.auto.last_successful_scan_at";
    public static readonly TimeSpan DefaultOverlap = TimeSpan.FromMinutes(10);

    public static AutomaticScanWindowPlan Plan(
        DateTimeOffset now,
        int recentScanDays,
        string? lastSuccessfulScanValue,
        TimeSpan? overlap = null)
    {
        var boundedDays = Math.Clamp(recentScanDays, 1, 90);
        var fullWindowStart = now.AddDays(-boundedDays);
        var effectiveOverlap = overlap ?? DefaultOverlap;
        if (effectiveOverlap < TimeSpan.Zero)
        {
            effectiveOverlap = TimeSpan.Zero;
        }

        if (!DateTimeOffset.TryParse(lastSuccessfulScanValue, out var lastSuccessfulScan))
        {
            return new AutomaticScanWindowPlan(fullWindowStart, UsedLastSuccessfulScan: false);
        }

        var overlappedStart = lastSuccessfulScan.ToOffset(now.Offset).Subtract(effectiveOverlap);
        if (overlappedStart > now)
        {
            return new AutomaticScanWindowPlan(now.Subtract(effectiveOverlap), UsedLastSuccessfulScan: false);
        }

        return new AutomaticScanWindowPlan(
            overlappedStart < fullWindowStart ? fullWindowStart : overlappedStart,
            UsedLastSuccessfulScan: overlappedStart >= fullWindowStart);
    }
}
