namespace MailWhere.Core.Capabilities;

public sealed record RecentMailRangeChoice(int Days);

public static class RecentMailRangeChoices
{
    public static IReadOnlyList<RecentMailRangeChoice> All { get; } =
    [
        new(1),
        new(7),
        new(30),
        new(90)
    ];

    public static int DefaultDays => 30;

    public static int NormalizeDays(int days)
    {
        if (All.Any(choice => choice.Days == days))
        {
            return days;
        }

        return days <= 1
            ? 1
            : days <= 7
                ? 7
                : days <= 30
                ? 30
                : 90;
    }
}

public enum ReminderNotificationMode
{
    Off,
    DueToday,
    DayBefore
}

public sealed record ReminderNotificationChoice(ReminderNotificationMode Mode, int LookAheadHours);

public static class ReminderNotificationChoices
{
    public static IReadOnlyList<ReminderNotificationChoice> All { get; } =
    [
        new(ReminderNotificationMode.Off, 0),
        new(ReminderNotificationMode.DueToday, 1),
        new(ReminderNotificationMode.DayBefore, 24)
    ];

    public static ReminderNotificationMode DefaultMode => ReminderNotificationMode.DayBefore;

    public static int ToLookAheadHours(ReminderNotificationMode mode) =>
        All.FirstOrDefault(choice => choice.Mode == mode)?.LookAheadHours
        ?? All.First(choice => choice.Mode == DefaultMode).LookAheadHours;

    public static ReminderNotificationMode FromLookAheadHours(int lookAheadHours) =>
        lookAheadHours <= 0
            ? ReminderNotificationMode.Off
            : lookAheadHours <= 1
                ? ReminderNotificationMode.DueToday
                : ReminderNotificationMode.DayBefore;
}
