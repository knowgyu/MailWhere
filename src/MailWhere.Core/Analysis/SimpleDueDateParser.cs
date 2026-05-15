using System.Text.RegularExpressions;

namespace MailWhere.Core.Analysis;

public static class SimpleDueDateParser
{
    private static readonly Regex IsoDate = new("(?<year>20\\d{2})[-./](?<month>\\d{1,2})[-./](?<day>\\d{1,2})", RegexOptions.Compiled);
    private static readonly Regex KoreanMonthDay = new("(?<month>\\d{1,2})\\s*월\\s*(?<day>\\d{1,2})\\s*일", RegexOptions.Compiled);
    private static readonly Regex KoreanDayOnlyDeadline = new("(?<day>\\d{1,2})\\s*일?\\s*(?:까지|전까지|내로)", RegexOptions.Compiled);
    private static readonly Regex Weekday = new(
        "(?<next>다음\\s*주|next\\s+week)?\\s*(?<day>월요일|화요일|수요일|목요일|금요일|토요일|일요일|monday|tuesday|wednesday|thursday|friday|saturday|sunday)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DateTimeOffset? TryParse(string text, DateTimeOffset anchor)
    {
        var iso = IsoDate.Match(text);
        if (iso.Success && TryDate(
            int.Parse(iso.Groups["year"].Value),
            int.Parse(iso.Groups["month"].Value),
            int.Parse(iso.Groups["day"].Value),
            anchor.Offset,
            out var parsedIso))
        {
            return parsedIso;
        }

        var md = KoreanMonthDay.Match(text);
        if (md.Success && TryDate(
            anchor.Year,
            int.Parse(md.Groups["month"].Value),
            int.Parse(md.Groups["day"].Value),
            anchor.Offset,
            out var parsedMonthDay))
        {
            return parsedMonthDay;
        }

        var dayOnly = KoreanDayOnlyDeadline.Match(text);
        if (dayOnly.Success)
        {
            var day = int.Parse(dayOnly.Groups["day"].Value);
            var month = anchor.Month;
            var year = anchor.Year;
            if (day < anchor.Day)
            {
                var nextMonth = new DateTimeOffset(anchor.Year, anchor.Month, 1, 0, 0, 0, anchor.Offset).AddMonths(1);
                year = nextMonth.Year;
                month = nextMonth.Month;
            }

            if (TryDate(year, month, day, anchor.Offset, out var parsedDayOnly))
            {
                return parsedDayOnly;
            }
        }

        if (text.Contains("내일", StringComparison.OrdinalIgnoreCase) || text.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return AtLocal(anchor, daysToAdd: 1, hour: 9);
        }

        if (text.Contains("오늘", StringComparison.OrdinalIgnoreCase) || text.Contains("today", StringComparison.OrdinalIgnoreCase) || text.Contains("EOD", StringComparison.OrdinalIgnoreCase))
        {
            return AtLocal(anchor, daysToAdd: 0, hour: 18);
        }

        var weekday = Weekday.Match(text);
        if (weekday.Success && TryWeekday(weekday.Groups["day"].Value, out var dayOfWeek))
        {
            var hasNextWeekPrefix = weekday.Groups["next"].Success;
            var daysToAdd = hasNextWeekPrefix
                ? DaysUntilNextWeekday(anchor, dayOfWeek)
                : DaysUntil(anchor.DayOfWeek, dayOfWeek);
            return AtLocal(anchor, daysToAdd, hour: 9);
        }

        return null;
    }

    private static DateTimeOffset AtLocal(DateTimeOffset anchor, int daysToAdd, int hour)
    {
        return new DateTimeOffset(anchor.Year, anchor.Month, anchor.Day, hour, 0, 0, anchor.Offset).AddDays(daysToAdd);
    }

    private static bool TryDate(int year, int month, int day, TimeSpan offset, out DateTimeOffset value)
    {
        try
        {
            value = new DateTimeOffset(year, month, day, 9, 0, 0, offset);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static int DaysUntil(DayOfWeek from, DayOfWeek to) =>
        ((int)to - (int)from + 7) % 7;

    private static int DaysUntilNextWeekday(DateTimeOffset anchor, DayOfWeek to)
    {
        var daysUntilNextMonday = DaysUntil(anchor.DayOfWeek, DayOfWeek.Monday);
        if (daysUntilNextMonday == 0)
        {
            daysUntilNextMonday = 7;
        }

        return daysUntilNextMonday + DaysUntil(DayOfWeek.Monday, to);
    }

    private static bool TryWeekday(string value, out DayOfWeek dayOfWeek)
    {
        var normalized = value.Trim().ToLowerInvariant();
        dayOfWeek = normalized switch
        {
            "월요일" or "monday" => DayOfWeek.Monday,
            "화요일" or "tuesday" => DayOfWeek.Tuesday,
            "수요일" or "wednesday" => DayOfWeek.Wednesday,
            "목요일" or "thursday" => DayOfWeek.Thursday,
            "금요일" or "friday" => DayOfWeek.Friday,
            "토요일" or "saturday" => DayOfWeek.Saturday,
            "일요일" or "sunday" => DayOfWeek.Sunday,
            _ => default
        };
        return normalized is
            "월요일" or "monday" or
            "화요일" or "tuesday" or
            "수요일" or "wednesday" or
            "목요일" or "thursday" or
            "금요일" or "friday" or
            "토요일" or "saturday" or
            "일요일" or "sunday";
    }
}
