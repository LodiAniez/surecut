using System.Windows;
using System.Windows.Threading;
using SureCut.Services;

namespace SureCut;

public partial class App : Application
{
    private SingleInstance? _single;
    private ConfigStore? _store;
    private ThemeService? _theme;
    private LauncherHost? _host;
    private readonly Queue<DateTime> _recentFailures = new();

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SureCut");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Init(DataFolder);

        _single = new SingleInstance();
        if (!_single.TryAcquire())
        {
            // LIFE-1: hand over to the running copy and leave.
            SingleInstance.SignalRunningInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled exception (non-UI thread)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Logger.Info($"SureCut starting. exe={Environment.ProcessPath}");

        _store = new ConfigStore(DataFolder);
        var config = _store.Load();
        _theme = new ThemeService();
        _host = new LauncherHost(_store, config, _theme);
        _host.Start();
    }

    /// <summary>LIFE-2: log, keep running, but give up on a crash loop (3 within 60 s).</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled exception (UI thread)", e.Exception);

        var now = DateTime.UtcNow;
        _recentFailures.Enqueue(now);
        while (_recentFailures.Count > 0 && (now - _recentFailures.Peek()) > TimeSpan.FromSeconds(60)) _recentFailures.Dequeue();

        if (_recentFailures.Count >= 3)
        {
            Logger.Error("Crash loop detected; exiting.");
            e.Handled = false;
            return;
        }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Dispose();
            _store?.Flush();
            _theme?.Dispose();
        }
        finally
        {
            _single?.Dispose();
            Logger.Info("SureCut exited.");
        }
        base.OnExit(e);
    }
}
