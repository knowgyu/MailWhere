namespace MailWhere.Core.Domain;

public enum FollowUpDisplayCategory
{
    ActionForMe,
    WaitingOnThem,
    ConfirmationCandidate
}

public static class FollowUpPresentation
{
    private static readonly string[] ActionTitlePrefixes =
    [
        "메일 확인:",
        "메일 확인 ·",
        "LLM 분석 확인 필요:",
        "오늘 회신 ·",
        "오늘 회신:",
        "Action required:",
        "Action required",
        "D-day ·",
        "할 일 ·",
        "대기 ·",
        "일정 ·",
        "[할 일]",
        "[대기]",
        "[일정]"
    ];

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

    public static string ActionTitle(string? title)
    {
        var compact = EvidencePolicy.Truncate(title);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return "메일 확인";
        }

        compact = string.Join(' ', compact.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var prefix in ActionTitlePrefixes)
            {
                if (!compact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                compact = compact[prefix.Length..].Trim(' ', '·', '-', ':');
                changed = true;
            }
        }

        return string.IsNullOrWhiteSpace(compact) ? "요청 내용 확인" : compact;
    }

    public static bool IsActive(LocalTaskItem task) =>
        task.Status is LocalTaskStatus.Open or LocalTaskStatus.Snoozed;

    public static bool IsVisibleInPrimary(LocalTaskItem task, DateTimeOffset now) =>
        IsActive(task) && !IsSnoozedForFuture(task, now);

    public static bool IsSnoozedForFuture(LocalTaskItem task, DateTimeOffset now) =>
        task.Status == LocalTaskStatus.Snoozed
        && task.SnoozeUntil is not null
        && task.SnoozeUntil.Value > now;
}
