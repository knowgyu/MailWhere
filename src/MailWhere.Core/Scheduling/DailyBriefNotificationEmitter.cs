using MailWhere.Core.Notifications;
using MailWhere.Core.Storage;

namespace MailWhere.Core.Scheduling;

public static class DailyBriefNotificationEmitter
{
    public static UserNotification CreateNotification(DailyBriefSnapshot snapshot, DailyBoardPlan plan)
    {
        var message = snapshot.TotalHighlights == 0
            ? "오늘 바로 볼 항목은 없습니다. 업무 보드에서 전체 흐름을 확인할 수 있습니다."
            : $"할 일 {snapshot.ActionItems.Count}개 · 대기 {snapshot.WaitingItems.Count}개 · 검토 후보 {snapshot.HiddenCandidateCount}개";

        return new UserNotification(
            UserNotificationKind.DailyBrief,
            "오늘 브리핑",
            message,
            $"daily-brief:{plan.TodayKey}");
    }

    public static async Task EmitAndMarkShownAsync(
        IUserNotificationSink notificationSink,
        IAppStateStore appStateStore,
        DailyBoardPlan plan,
        DailyBriefSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await notificationSink.ShowAsync(CreateNotification(snapshot, plan), cancellationToken).ConfigureAwait(false);
        await appStateStore.SetAppStateAsync(DailyBoardPlanner.LastShownDateKey, plan.TodayKey, cancellationToken).ConfigureAwait(false);
    }
}
