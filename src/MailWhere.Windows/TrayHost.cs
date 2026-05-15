using System.Drawing;
using System.Windows;
using MailWhere.Core.Notifications;
using Forms = System.Windows.Forms;

namespace MailWhere.Windows;

public sealed class TrayHost : IDisposable, IUserNotificationSink
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly ToastNotificationHost _toastHost;

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
        _notifyIcon.BalloonTipClicked += async (_, _) => await ShowDailyBoardAsync();
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
            var icon = notification.Kind == UserNotificationKind.Error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(8000, notification.Title, notification.Message, icon);
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowWindow());
        menu.Items.Add("오늘의 업무 보드", null, async (_, _) => await ShowDailyBoardAsync());
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

    private async Task ShowDailyBoardAsync()
    {
        ShowWindow();
        await _window.OpenDailyBoardAsync();
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
