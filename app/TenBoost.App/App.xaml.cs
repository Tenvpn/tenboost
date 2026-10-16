using System.Windows;

namespace TenBoost.App;

public partial class App : Application
{
    /// <summary>Автозапуск при входе в Windows: сразу в трей, без окна.</summary>
    public const string TrayArgument = "--tray";

    private const string MutexName = @"Local\TenBoost.Tray";
    private const string ShowEventName = @"Local\TenBoost.Show";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private StatusHub? _hub;
    private TrayController? _tray;
    private MainWindow? _main;
    private AboutWindow? _about;

    internal void ShowMain()
    {
        if (_hub is null)
        {
            return;
        }

        _main ??= new MainWindow(_hub);
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
    }

    internal void ShowAbout()
    {
        if (_about is { IsLoaded: true })
        {
            _about.Activate();
            return;
        }

        _about = new AboutWindow(_hub?.Last?.EngineVersion);
        _about.Closed += (_, _) => _about = null;
        _about.Show();
        _about.Activate();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created)
        {
            // Уже запущен: просим первый экземпляр показать окно и выходим.
            _showSignal.Set();
            Shutdown();
            return;
        }

        _hub = new StatusHub();
        _tray = new TrayController(this, _hub);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showSignal, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, Timeout.Infinite, executeOnlyOnce: false);

        if (!e.Args.Contains(TrayArgument))
        {
            ShowMain();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        if (_main is not null)
        {
            _main.Quitting = true;
            _main.Close();
        }

        _tray?.Dispose();
        _hub?.Dispose();
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
