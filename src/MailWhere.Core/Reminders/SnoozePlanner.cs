namespace MailWhere.Core.Reminders;

public enum SnoozePreset
{
    TodayAtOnePm,
    TomorrowMorning,
    NextMondayMorning
}

public static class SnoozePlanner
{
    public static DateTimeOffset Plan(SnoozePreset preset, DateTimeOffset now) => preset switch
    {
        SnoozePreset.TodayAtOnePm => NextOnePm(now),
        SnoozePreset.TomorrowMorning => AtLocalDate(now.Date.AddDays(1), now.Offset, 9),
        SnoozePreset.NextMondayMorning => AtLocalDate(NextMonday(now), now.Offset, 9),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    private static DateTimeOffset NextOnePm(DateTimeOffset now)
    {
        var todayAtOne = AtLocalDate(now.Date, now.Offset, 13);
        return todayAtOne > now ? todayAtOne : todayAtOne.AddDays(1);
    }

    private static DateTimeOffset AtLocalDate(DateTime date, TimeSpan offset, int hour) =>
        new(date.Year, date.Month, date.Day, hour, 0, 0, offset);

    private static DateTime NextMonday(DateTimeOffset now)
    {
        var days = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (days == 0)
        {
            days = 7;
        }

        return now.Date.AddDays(days);
    }
}
