namespace MailWhere.Core.Scheduling;

public sealed record DailyBoardOpenOptions(
    BoardRouteFilter Filter,
    bool ShowBriefSummary,
    BoardOrigin Origin,
    bool BringToFront)
{
    public static DailyBoardOpenOptions ManualAll(bool bringToFront = true) =>
        new(BoardRouteFilter.All, ShowBriefSummary: false, BoardOrigin.Manual, bringToFront);

    public static DailyBoardOpenOptions TodayBrief(BoardOrigin origin, bool bringToFront = true) =>
        new(BoardRouteFilter.Today, ShowBriefSummary: true, origin, bringToFront);
}

public enum BoardRouteFilter
{
    All,
    Today,
    Week,
    Month,
    NoDue
}

public enum BoardOrigin
{
    Manual,
    TrayToday,
    DailyBriefToast,
    ScheduledDailyBoard,
    ScheduledBriefFallback
}
