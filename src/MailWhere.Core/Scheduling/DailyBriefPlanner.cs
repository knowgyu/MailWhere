using MailWhere.Core.Domain;

namespace MailWhere.Core.Scheduling;

public sealed record DailyBriefSnapshot(
    IReadOnlyList<LocalTaskItem> ActionItems,
    IReadOnlyList<LocalTaskItem> WaitingItems,
    int HiddenCandidateCount)
{
    public int TotalHighlights => ActionItems.Count + WaitingItems.Count;
}

public sealed record DailyBriefOptions(
    TimeSpan? WaitingAgedAfter = null,
    double NewImportantConfidence = 0.78)
{
    public TimeSpan EffectiveWaitingAgedAfter => WaitingAgedAfter ?? TimeSpan.FromDays(3);
}

public static class DailyBriefPlanner
{
    public static DailyBriefSnapshot Build(
        IEnumerable<LocalTaskItem> tasks,
        IEnumerable<ReviewCandidate> candidates,
        DateTimeOffset now,
        DailyBriefOptions? options = null)
    {
        options ??= new DailyBriefOptions();

        var highlights = tasks
            .Where(FollowUpPresentation.IsActive)
            .Where(task => !FollowUpPresentation.IsSnoozedForFuture(task, now))
            .Where(task => ShouldHighlight(task, now, options))
            .OrderBy(task => task.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(task => task.Confidence)
            .ThenBy(task => task.CreatedAt)
            .ToArray();

        var actionItems = highlights
            .Where(task => FollowUpPresentation.CategoryFor(task) == FollowUpDisplayCategory.ActionForMe)
            .ToArray();
        var waitingItems = highlights
            .Where(task => FollowUpPresentation.CategoryFor(task) == FollowUpDisplayCategory.WaitingOnThem)
            .ToArray();
        var hiddenCandidateCount = candidates.Count(candidate => !candidate.Suppressed
            && (candidate.SnoozeUntil is null || candidate.SnoozeUntil <= now));

        return new DailyBriefSnapshot(actionItems, waitingItems, hiddenCandidateCount);
    }

    public static bool ShouldHighlight(LocalTaskItem task, DateTimeOffset now, DailyBriefOptions? options = null)
    {
        options ??= new DailyBriefOptions();

        if (!FollowUpPresentation.IsActive(task) || FollowUpPresentation.IsSnoozedForFuture(task, now))
        {
            return false;
        }

        if (task.SnoozeUntil is not null && task.SnoozeUntil.Value <= now)
        {
            return true;
        }

        if (task.DueAt is not null && task.DueAt.Value.Date <= now.Date)
        {
            return true;
        }

        if (task.CreatedAt.Date == now.Date && task.Confidence >= options.NewImportantConfidence)
        {
            return true;
        }

        return task.Kind == FollowUpKind.WaitingForReply
               && now - task.CreatedAt >= options.EffectiveWaitingAgedAfter;
    }
}
