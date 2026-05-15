using MailWhere.Core.Domain;

namespace MailWhere.Core.Reminders;

public sealed record ReminderRule(string Code, TimeSpan BeforeDue, string KoreanLabel)
{
    public static IReadOnlyList<ReminderRule> DefaultRules { get; } = new[]
    {
        new ReminderRule("D-7", TimeSpan.FromDays(7), "마감 7일 전"),
        new ReminderRule("D-1", TimeSpan.FromDays(1), "마감 하루 전"),
        new ReminderRule("D-day", TimeSpan.Zero, "마감 당일")
    };
}

public sealed record ReminderCandidate(
    Guid TaskId,
    string ReminderKey,
    DateTimeOffset NotifyAt,
    string Title,
    string DdayLabel,
    string Reason);

public static class ReminderPlanner
{
    public static IReadOnlyList<ReminderCandidate> Plan(LocalTaskItem task, DateTimeOffset now, IReadOnlyList<ReminderRule>? rules = null)
    {
        if (task.DueAt is null || task.Status is LocalTaskStatus.Done or LocalTaskStatus.Dismissed or LocalTaskStatus.NotATask)
        {
            return Array.Empty<ReminderCandidate>();
        }

        var dueAt = task.DueAt.Value;
        var results = new List<ReminderCandidate>();
        foreach (var rule in rules ?? ReminderRule.DefaultRules)
        {
            var notifyAt = dueAt - rule.BeforeDue;
            if (notifyAt < now.AddMinutes(-1))
            {
                continue;
            }

            results.Add(new ReminderCandidate(
                task.Id,
                $"{task.Id:N}:{rule.Code}",
                notifyAt,
                task.Title,
                DdayFormatter.Format(dueAt, now),
                rule.KoreanLabel));
        }

        return results.OrderBy(item => item.NotifyAt).ToArray();
    }

    public static IReadOnlyList<ReminderCandidate> DueForNotification(IEnumerable<LocalTaskItem> tasks, DateTimeOffset now, TimeSpan lookAhead)
    {
        var until = now + lookAhead;
        return tasks.SelectMany(task => Plan(task, now))
            .Where(candidate => candidate.NotifyAt <= until)
            .OrderBy(candidate => candidate.NotifyAt)
            .ToArray();
    }
}

public static class DdayFormatter
{
    public static string Format(DateTimeOffset dueAt, DateTimeOffset now)
    {
        var dueDate = dueAt.Date;
        var nowDate = now.Date;
        var days = (dueDate - nowDate).Days;
        return days switch
        {
            > 0 => $"D-{days}",
            0 => "D-day",
            _ => $"D+{Math.Abs(days)}"
        };
    }
}
