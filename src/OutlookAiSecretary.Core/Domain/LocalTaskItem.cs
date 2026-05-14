namespace OutlookAiSecretary.Core.Domain;

public enum LocalTaskStatus
{
    Open,
    Snoozed,
    Done,
    Dismissed,
    NotATask
}

public sealed record LocalTaskItem(
    Guid Id,
    string Title,
    DateTimeOffset? DueAt,
    string? SourceIdHash,
    double Confidence,
    string Reason,
    string? EvidenceSnippet,
    LocalTaskStatus Status,
    DateTimeOffset? SnoozeUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool SourceDerivedDataDeleted = false)
{
    public const string RedactedTitle = "메일 기반 항목(원문 삭제됨)";
    public const string RedactedReason = "메일 원문 기반 사유가 삭제되었습니다.";

    public static LocalTaskItem FromAnalysis(EmailSnapshot source, FollowUpAnalysis analysis, DateTimeOffset now)
    {
        var title = string.IsNullOrWhiteSpace(analysis.SuggestedTitle)
            ? "메일 후속 조치"
            : EvidencePolicy.Truncate(analysis.SuggestedTitle.Trim()) ?? "메일 후속 조치";

        return new LocalTaskItem(
            Guid.NewGuid(),
            title,
            analysis.DueAt,
            source.SourceHash,
            analysis.Confidence,
            EvidencePolicy.Truncate(analysis.Reason) ?? "메일 후속 조치 분석",
            EvidencePolicy.Truncate(analysis.EvidenceSnippet),
            LocalTaskStatus.Open,
            null,
            now,
            now);
    }

    public LocalTaskItem MarkDone(DateTimeOffset now) => this with
    {
        Status = LocalTaskStatus.Done,
        UpdatedAt = now,
        SnoozeUntil = null
    };

    public LocalTaskItem SnoozeUntilTime(DateTimeOffset until, DateTimeOffset now) => this with
    {
        Status = LocalTaskStatus.Snoozed,
        SnoozeUntil = until,
        UpdatedAt = now
    };

    public LocalTaskItem DeleteSourceDerivedData(DateTimeOffset now) => this with
    {
        Title = RedactedTitle,
        Reason = RedactedReason,
        EvidenceSnippet = null,
        SourceDerivedDataDeleted = true,
        UpdatedAt = now
    };
}
