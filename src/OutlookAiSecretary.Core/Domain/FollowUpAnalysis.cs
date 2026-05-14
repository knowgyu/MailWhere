namespace OutlookAiSecretary.Core.Domain;

public enum FollowUpKind
{
    None,
    ReplyRequired,
    ActionRequested,
    Deadline,
    WaitingForReply,
    ReviewNeeded,
    Meeting,
    CalendarEvent
}

public enum AnalysisDisposition
{
    Ignore,
    Review,
    AutoCreateTask
}

public sealed record FollowUpAnalysis(
    FollowUpKind Kind,
    AnalysisDisposition Disposition,
    double Confidence,
    string SuggestedTitle,
    string Reason,
    string? EvidenceSnippet,
    DateTimeOffset? DueAt,
    string? Summary = null)
{
    public static FollowUpAnalysis Ignore(string reason) => new(
        FollowUpKind.None,
        AnalysisDisposition.Ignore,
        0,
        string.Empty,
        reason,
        null,
        null);
}
