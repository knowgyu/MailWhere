using MailWhere.Core.Domain;

namespace MailWhere.Core.Scheduling;

public static class DailyBoardRouteTaskSelector
{
    public static IReadOnlyList<LocalTaskItem> SelectVisibleTasks(
        IEnumerable<LocalTaskItem> tasks,
        IEnumerable<ReviewCandidate> candidates,
        DateTimeOffset now,
        BoardRouteFilter filter,
        bool showBriefSummary)
    {
        var activeTasks = tasks.Where(task => FollowUpPresentation.IsVisibleInPrimary(task, now)).ToArray();
        var filtered = ApplyFilter(activeTasks, now, filter).ToList();

        if (showBriefSummary && filter == BoardRouteFilter.Today)
        {
            var ids = filtered.Select(task => task.Id).ToHashSet();
            var brief = DailyBriefPlanner.Build(activeTasks, candidates, now);
            foreach (var task in brief.ActionItems.Concat(brief.WaitingItems))
            {
                if (ids.Add(task.Id))
                {
                    filtered.Add(task);
                }
            }
        }

        return filtered;
    }

    private static IEnumerable<LocalTaskItem> ApplyFilter(IEnumerable<LocalTaskItem> tasks, DateTimeOffset now, BoardRouteFilter filter) =>
        filter switch
        {
            BoardRouteFilter.All => tasks,
            BoardRouteFilter.Today => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.ToOffset(now.Offset).Date <= now.Date),
            BoardRouteFilter.Week => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.ToOffset(now.Offset).Date <= FollowUpPresentation.EndOfKoreanWeek(now.Date)),
            BoardRouteFilter.Month => tasks.Where(task => task.DueAt is not null && task.DueAt.Value.ToOffset(now.Offset).Date <= now.AddDays(30).Date),
            BoardRouteFilter.NoDue => tasks.Where(task => task.DueAt is null),
            _ => tasks
        };
}
