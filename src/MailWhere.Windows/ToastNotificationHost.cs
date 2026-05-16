using System.Windows;
using MailWhere.Core.Notifications;
using MailWhere.Core.Scheduling;

namespace MailWhere.Windows;

public sealed class ToastNotificationHost : IUserNotificationSink, IDisposable
{
    private const int MaxVisibleToasts = 4;
    private const double ScreenMargin = 18;
    private const double ToastGap = 10;

    private readonly MainWindow _mainWindow;
    private readonly List<ToastNotificationWindow> _toasts = [];

    public ToastNotificationHost(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mainWindow.Dispatcher.CheckAccess())
        {
            ShowOnUiThread(notification);
            return;
        }

        await _mainWindow.Dispatcher.InvokeAsync(() => ShowOnUiThread(notification));
    }

    public void Dispose()
    {
        foreach (var toast in _toasts.ToArray())
        {
            toast.ToastClosed -= ToastClosed;
            toast.Close();
        }

        _toasts.Clear();
    }

    private void ShowOnUiThread(UserNotification notification)
    {
        while (_toasts.Count >= MaxVisibleToasts)
        {
            var stale = _toasts[0];
            _toasts.RemoveAt(0);
            stale.ToastClosed -= ToastClosed;
            stale.Close();
        }

        var spec = CreateSpec(notification);
        var toast = new ToastNotificationWindow(
            spec,
            () => RunPrimaryActionAsync(notification),
            ResolveSecondaryAction(notification));

        toast.ToastClosed += ToastClosed;
        _toasts.Add(toast);
        toast.Show();
        toast.UpdateLayout();
        Reflow();
    }

    private void ToastClosed(object? sender, EventArgs e)
    {
        if (sender is ToastNotificationWindow toast)
        {
            toast.ToastClosed -= ToastClosed;
            _toasts.Remove(toast);
            Reflow();
        }
    }

    private void Reflow()
    {
        var workArea = SystemParameters.WorkArea;
        var bottom = workArea.Bottom - ScreenMargin;

        for (var index = _toasts.Count - 1; index >= 0; index--)
        {
            var toast = _toasts[index];
            toast.UpdateLayout();
            var height = toast.StackHeight;
            bottom -= height;
            var left = workArea.Right - toast.Width - ScreenMargin;
            var top = Math.Max(workArea.Top + ScreenMargin, bottom);
            toast.MoveTo(left, top);
            bottom -= ToastGap;
        }
    }

    private async Task RunPrimaryActionAsync(UserNotification notification)
    {
        switch (NotificationActionResolver.Resolve(notification.Kind).PrimaryTarget)
        {
            case NotificationPrimaryActionTarget.OpenDailyBoard:
                await _mainWindow.OpenDailyBoardAsync();
                break;

            case NotificationPrimaryActionTarget.OpenDailyBoardTodayBrief:
                WindowsRuntimeDiagnostics.RecordUiEvent("daily-brief-cta-opened-today-brief", new Dictionary<string, string>
                {
                    ["origin"] = BoardOrigin.DailyBriefToast.ToString()
                });
                await _mainWindow.OpenDailyBoardTodayAsync(showBriefSummary: true, BoardOrigin.DailyBriefToast);
                break;

            default:
                ShowMainWindow();
                break;
        }
    }

    private Func<Task>? ResolveSecondaryAction(UserNotification notification)
    {
        var actionPlan = NotificationActionResolver.Resolve(notification.Kind);
        return actionPlan.SecondaryTarget switch
        {
            NotificationSecondaryActionTarget.OpenReviewTab => () =>
            {
                _mainWindow.OpenReviewTab();
                return Task.CompletedTask;
            },
            _ => null
        };
    }

    private void ShowMainWindow()
    {
        _mainWindow.ShowShell();
    }

    private static ToastNotificationSpec CreateSpec(UserNotification notification)
    {
        return notification.Kind switch
        {
            UserNotificationKind.DailyBrief => new ToastNotificationSpec(
                "오늘 브리핑",
                notification.Title,
                notification.Message,
                "업무 보드에서 계속 관리합니다.",
                "☀",
                "#2458F2",
                "#EEF3FF",
                "오늘 업무 보기",
                null,
                TimeSpan.FromSeconds(14)),

            UserNotificationKind.Reminder => new ToastNotificationSpec(
                "리마인더",
                notification.Title,
                notification.Message,
                "업무 보드에서 마감과 근거를 확인하세요",
                "!",
                "#F79009",
                "#FFF6E5",
                "업무 보드",
                null,
                TimeSpan.FromSeconds(14)),

            UserNotificationKind.ScanSummary => new ToastNotificationSpec(
                "메일 확인 완료",
                notification.Title,
                notification.Message,
                "검토 후보는 보드의 검토 후보 탭에 모아둡니다",
                "✓",
                "#2458F2",
                "#EEF3FF",
                "업무 보드",
                "검토 후보",
                TimeSpan.FromSeconds(12)),

            UserNotificationKind.Error => new ToastNotificationSpec(
                "확인 필요",
                notification.Title,
                notification.Message,
                "앱을 열어 상태와 문제 해결 정보를 확인하세요",
                "×",
                "#D92D20",
                "#FEE4E2",
                "앱 열기",
                null,
                TimeSpan.FromSeconds(16)),

            _ => new ToastNotificationSpec(
                "MailWhere",
                notification.Title,
                notification.Message,
                "클릭하면 MailWhere를 엽니다",
                "i",
                "#6172F3",
                "#EEF4FF",
                "앱 열기",
                null,
                TimeSpan.FromSeconds(10))
        };
    }
}
