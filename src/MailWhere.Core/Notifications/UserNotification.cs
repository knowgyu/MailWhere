namespace MailWhere.Core.Notifications;

public enum UserNotificationKind
{
    ScanSummary,
    Reminder,
    DailyBrief,
    Diagnostics,
    Error
}

public sealed record UserNotification(
    UserNotificationKind Kind,
    string Title,
    string Message,
    string? DeduplicationKey = null);

public interface IUserNotificationSink
{
    Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default);
}

public sealed class NullNotificationSink : IUserNotificationSink
{
    public Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
