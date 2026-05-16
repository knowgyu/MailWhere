using System.Windows;

namespace MailWhere.Windows;

public partial class App : System.Windows.Application
{
    private TrayHost? _trayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        _trayHost = new TrayHost(mainWindow);
        mainWindow.SetNotificationSink(_trayHost);
        _ = StartBackgroundSafelyAsync(mainWindow);
    }

    private static async Task StartBackgroundSafelyAsync(MainWindow mainWindow)
    {
        try
        {
            await mainWindow.StartBackgroundAsync();
        }
        catch (Exception ex)
        {
            mainWindow.ReportStatus($"트레이 초기화 중 문제가 발생했습니다: {ex.GetType().Name}");
            mainWindow.ShowShell();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayHost?.Dispose();
        base.OnExit(e);
    }
}
