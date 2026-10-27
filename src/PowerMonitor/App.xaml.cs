using System.Windows;
using PowerMonitor.Services;

namespace PowerMonitor;

public partial class App : Application
{
    private MonitorEngine? _engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _engine = new MonitorEngine();
        var window = new MainWindow(_engine);
        window.Show();

        _engine.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        base.OnExit(e);
    }
}
