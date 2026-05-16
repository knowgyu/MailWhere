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

    public static string HumanDueText(DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (dueAt is null)
        {
            return "날짜 없음";
        }

        var localNow = now;
        var value = dueAt.Value.ToOffset(localNow.Offset);
        var date = value.Date;
        var today = localNow.Date;

        if (date == today)
        {
            return $"오늘 {value:HH:mm}";
        }

        if (date == today.AddDays(1))
        {
            return $"내일 {value:HH:mm}";
        }

        if (date > today && StartOfKoreanWeek(date) == StartOfKoreanWeek(today))
        {
            return $"이번 주 {KoreanDayOfWeek(date.DayOfWeek)} {value:HH:mm}";
        }

        return $"{value:M/d HH:mm}";
    }

    public static string HumanSenderText(string? senderDisplay, string fallback = "직접 추가")
    {
        var sender = string.IsNullOrWhiteSpace(senderDisplay)
            ? fallback
            : string.Join(' ', senderDisplay.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        var compact = sender.Length <= 30 ? sender : sender[..30].TrimEnd() + "…";
        return $"보낸 사람: {compact}";
    }

    public static DateTime StartOfKoreanWeek(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-diff);
    }

    public static DateTime EndOfKoreanWeek(DateTime date) =>
        StartOfKoreanWeek(date).AddDays(6);

    private static string KoreanDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "월요일",
        DayOfWeek.Tuesday => "화요일",
        DayOfWeek.Wednesday => "수요일",
        DayOfWeek.Thursday => "목요일",
        DayOfWeek.Friday => "금요일",
        DayOfWeek.Saturday => "토요일",
        DayOfWeek.Sunday => "일요일",
        _ => string.Empty
    };
}
