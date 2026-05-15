namespace MailWhere.Core.Notifications;

public enum NotificationPrimaryActionTarget
{
    OpenApp,
    OpenDailyBoard,
    OpenDailyBoardTodayBrief
}

public enum NotificationSecondaryActionTarget
{
    None,
    OpenReviewTab
}

public sealed record NotificationActionPlan(
    NotificationPrimaryActionTarget PrimaryTarget,
    NotificationSecondaryActionTarget SecondaryTarget = NotificationSecondaryActionTarget.None);

public static class NotificationActionResolver
{
    public static NotificationActionPlan Resolve(UserNotificationKind kind) => kind switch
    {
        UserNotificationKind.DailyBrief => new NotificationActionPlan(
            NotificationPrimaryActionTarget.OpenDailyBoardTodayBrief),

        UserNotificationKind.Reminder => new NotificationActionPlan(
            NotificationPrimaryActionTarget.OpenDailyBoard),

        UserNotificationKind.ScanSummary => new NotificationActionPlan(
            NotificationPrimaryActionTarget.OpenDailyBoard,
            NotificationSecondaryActionTarget.OpenReviewTab),

        UserNotificationKind.Error => new NotificationActionPlan(
            NotificationPrimaryActionTarget.OpenApp),

        _ => new NotificationActionPlan(
            NotificationPrimaryActionTarget.OpenApp)
    };
}
