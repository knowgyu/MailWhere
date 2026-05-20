using MailWhere.Core.Domain;

namespace MailWhere.Core.Scheduling;

public sealed record WeeklyReviewSummary(
    DateTimeOffset Since,
    DateTimeOffset Until,
    int NewTaskCount,
    int ArchivedTaskCount,
    int OpenTaskCount,
    int ReviewCandidateCount,
    IReadOnlyList<LocalTaskItem> AgedWaitingItems,
    IReadOnlyList<WaitingClosureSuggestion> ClosureSuggestions)
{
    public string ToKoreanSummary()
    {
        var lines = new List<string>
        {
            $"이번 주 정리 ({Since:MM/dd}~{Until:MM/dd})",
            $"새 업무 {NewTaskCount}개 · 보관 {ArchivedTaskCount}개 · 열린 업무 {OpenTaskCount}개 · 확인 필요 {ReviewCandidateCount}개",
            $"오래 기다리는 중 {AgedWaitingItems.Count}개 · 보관 제안 {ClosureSuggestions.Count}개"
        };
        if (AgedWaitingItems.Count > 0)
        {
            lines.Add("\n오래 기다리는 중");
            lines.AddRange(AgedWaitingItems.Take(5).Select(item => $"- {FollowUpPresentation.ActionTitle(item.Title)}"));
        }

        if (ClosureSuggestions.Count > 0)
        {
            lines.Add("\n닫아도 될 수 있는 대기 항목");
            lines.AddRange(ClosureSuggestions.Take(5).Select(item => $"- {item.TaskTitle}: {item.ActionText}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class WeeklyReviewPlanner
{
    public static readonly TimeSpan DefaultAgedWaitingAfter = TimeSpan.FromDays(3);

    public WeeklyReviewSummary Build(
        IEnumerable<LocalTaskItem> openTasks,
        IEnumerable<LocalTaskItem> archivedTasks,
        IEnumerable<ReviewCandidate> reviewCandidates,
        IEnumerable<WaitingClosureSuggestion> closureSuggestions,
        DateTimeOffset now,
        TimeSpan? reviewWindow = null,
        TimeSpan? agedWaitingAfter = null)
    {
        var window = reviewWindow ?? TimeSpan.FromDays(7);
        var since = now.Subtract(window);
        var open = openTasks.ToArray();
        var archived = archivedTasks.ToArray();
        var agedAfter = agedWaitingAfter ?? DefaultAgedWaitingAfter;
        var agedWaiting = open
            .Where(task => task.Kind == FollowUpKind.WaitingForReply && now - task.CreatedAt >= agedAfter)
            .OrderBy(task => task.CreatedAt)
            .ToArray();

        return new WeeklyReviewSummary(
            since,
            now,
            open.Count(task => task.CreatedAt >= since),
            archived.Count(task => task.UpdatedAt >= since),
            open.Length,
            reviewCandidates.Count(),
            agedWaiting,
            closureSuggestions.ToArray());
    }
}
