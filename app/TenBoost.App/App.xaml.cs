using System.Windows;

namespace TenBoost.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TenBoost.Tray", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _tray = new TrayController(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
