using System.Drawing;
using System.Windows;
using MailWhere.Core.Notifications;
using MailWhere.Core.Scheduling;
using Forms = System.Windows.Forms;

namespace MailWhere.Windows;

public sealed class TrayHost : IDisposable, IUserNotificationSink
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ToastNotificationHost _toastHost;
    private UserNotification? _balloonFallbackNotification;

    public TrayHost(MainWindow window)
    {
        _window = window;
        _toastHost = new ToastNotificationHost(window);
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "MailWhere",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        _notifyIcon.BalloonTipClicked += async (_, _) => await RunBalloonPrimaryActionAsync();
    }

    public async Task ShowAsync(UserNotification notification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _toastHost.ShowAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _balloonFallbackNotification = notification;
            var icon = notification.Kind == UserNotificationKind.Error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(8000, notification.Title, notification.Message, icon);
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowWindow());
        menu.Items.Add("오늘 업무 보기", null, async (_, _) => await ShowTodayBoardAsync());
        menu.Items.Add("알림 테스트", null, async (_, _) => await ShowAsync(new UserNotification(UserNotificationKind.Reminder, "내일 마감 · 비용 자료 회신", "09:00까지 검토 후 회신이 필요합니다.", "tray-notification-test")));
        menu.Items.Add("종료", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task ShowTodayBoardAsync()
    {
        ShowWindow();
        WindowsRuntimeDiagnostics.RecordUiEvent("tray-today-route-opened-today-brief", new Dictionary<string, string>
        {
            ["origin"] = BoardOrigin.TrayToday.ToString()
        });
        await _window.OpenDailyBoardTodayAsync(showBriefSummary: true, BoardOrigin.TrayToday);
    }

    private async Task RunBalloonPrimaryActionAsync()
    {
        var notification = _balloonFallbackNotification;
        _balloonFallbackNotification = null;
        if (notification is null)
        {
            ShowWindow();
            return;
        }

        switch (NotificationActionResolver.Resolve(notification.Kind).PrimaryTarget)
        {
            case NotificationPrimaryActionTarget.OpenDailyBoardTodayBrief:
                ShowWindow();
                WindowsRuntimeDiagnostics.RecordUiEvent("daily-brief-cta-opened-today-brief", new Dictionary<string, string>
                {
                    ["origin"] = BoardOrigin.DailyBriefToast.ToString(),
                    ["surface"] = "tray-balloon-fallback"
                });
                await _window.OpenDailyBoardTodayAsync(showBriefSummary: true, BoardOrigin.DailyBriefToast);
                break;

            case NotificationPrimaryActionTarget.OpenDailyBoard:
                ShowWindow();
                await _window.OpenDailyBoardAsync();
                break;

            default:
                ShowWindow();
                break;
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fallback to the OS default below.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _toastHost.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
