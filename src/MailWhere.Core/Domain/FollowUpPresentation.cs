namespace MailWhere.Core.Domain;

public enum FollowUpDisplayCategory
{
    ActionForMe,
    WaitingOnThem,
    ConfirmationCandidate
}

public static class FollowUpPresentation
{
    public static FollowUpDisplayCategory CategoryFor(LocalTaskItem task) =>
        task.Kind == FollowUpKind.WaitingForReply
            ? FollowUpDisplayCategory.WaitingOnThem
            : FollowUpDisplayCategory.ActionForMe;

    public static FollowUpDisplayCategory CategoryFor(ReviewCandidate candidate) =>
        candidate.Analysis.Kind == FollowUpKind.WaitingForReply
            ? FollowUpDisplayCategory.WaitingOnThem
            : FollowUpDisplayCategory.ConfirmationCandidate;

    public static string KoreanTitle(FollowUpDisplayCategory category) => category switch
    {
        FollowUpDisplayCategory.ActionForMe => "내가 할 일",
        FollowUpDisplayCategory.WaitingOnThem => "기다리는 중",
        FollowUpDisplayCategory.ConfirmationCandidate => "검토 후보",
        _ => "기타"
    };

    public static string CompactBadge(FollowUpKind kind) => kind switch
    {
        FollowUpKind.Meeting or FollowUpKind.CalendarEvent => "일정",
        FollowUpKind.WaitingForReply => "대기",
        _ => "할 일"
    };

    public static bool IsActive(LocalTaskItem task) =>
        task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed;

    public static bool IsSnoozedForFuture(LocalTaskItem task, DateTimeOffset now) =>
        task.Status == LocalTaskStatus.Snoozed
        && task.SnoozeUntil is not null
        && task.SnoozeUntil.Value > now;
}
