namespace MailWhere.Core.Scheduling;

public sealed record DailyBoardPlan(
    bool ShouldShowNow,
    DateTimeOffset? NextShowAt,
    string TodayKey,
    string DailyBoardTime);

public static class DailyBoardPlanner
{
    public const string LastShownDateKey = "daily-board:last-shown-date";
    public const string DefaultDailyBoardTime = "08:00";
    public const int DefaultStartupSettlingDelayMinutes = 10;

    public static DailyBoardPlan Plan(
        DateTimeOffset now,
        string? dailyBoardTime,
        string? lastShownDateKey,
        DateTimeOffset? appStartedAt = null,
        TimeSpan? startupSettlingDelay = null)
    {
        var normalizedTime = NormalizeDailyBoardTime(dailyBoardTime);
        var todayKey = ToDateKey(now);
        var dailyTime = TimeOnly.Parse(normalizedTime);

        if (string.Equals(lastShownDateKey, todayKey, StringComparison.Ordinal))
        {
            return new DailyBoardPlan(false, NextDailyTime(now, dailyTime), todayKey, normalizedTime);
        }

        var scheduledToday = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            dailyTime.Hour,
            dailyTime.Minute,
            0,
            now.Offset);

        var earliestShowToday = scheduledToday;
        if (appStartedAt is not null)
        {
            var delay = startupSettlingDelay ?? TimeSpan.FromMinutes(DefaultStartupSettlingDelayMinutes);
            var settledAt = appStartedAt.Value.Add(delay);
            if (settledAt.Date == now.Date && settledAt > earliestShowToday)
            {
                earliestShowToday = settledAt;
            }
        }

        if (now < earliestShowToday)
        {
            return new DailyBoardPlan(false, earliestShowToday, todayKey, normalizedTime);
        }

        if (appStartedAt is not null)
        {
            return new DailyBoardPlan(true, now, todayKey, normalizedTime);
        }

        if (IsSameMinute(now, scheduledToday) || IsTopOfHour(now))
        {
            return new DailyBoardPlan(true, now, todayKey, normalizedTime);
        }

        return new DailyBoardPlan(false, NextTopOfHour(now), todayKey, normalizedTime);
    }

    public static string NormalizeDailyBoardTime(string? value)
    {
        if (TimeOnly.TryParse(value, out var parsed))
        {
            return parsed.ToString("HH:mm");
        }

        return DefaultDailyBoardTime;
    }

    public static string ToDateKey(DateTimeOffset value) => value.ToString("yyyy-MM-dd");

    private static bool IsTopOfHour(DateTimeOffset value) => value.Minute == 0;

    private static bool IsSameMinute(DateTimeOffset left, DateTimeOffset right) =>
        left.Year == right.Year
        && left.Month == right.Month
        && left.Day == right.Day
        && left.Hour == right.Hour
        && left.Minute == right.Minute;

    private static DateTimeOffset NextTopOfHour(DateTimeOffset value)
    {
        var floored = new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset);
        return floored.AddHours(1);
    }

    private static DateTimeOffset NextDailyTime(DateTimeOffset now, TimeOnly dailyTime)
    {
        var tomorrow = now.Date.AddDays(1);
        return new DateTimeOffset(
            tomorrow.Year,
            tomorrow.Month,
            tomorrow.Day,
            dailyTime.Hour,
            dailyTime.Minute,
            0,
            now.Offset);
    }
}
