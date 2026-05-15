using System.Windows;

namespace MailWhere.Windows;

public partial class App : System.Windows.Application
{
    private TrayHost? _trayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        _trayHost = new TrayHost(mainWindow);
        mainWindow.SetNotificationSink(_trayHost);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayHost?.Dispose();
        base.OnExit(e);
    }
}
