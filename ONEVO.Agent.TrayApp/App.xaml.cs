namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public partial class App : Microsoft.Maui.Controls.Application
{
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    private readonly TrayIconService _trayIcon;
    private readonly NamedPipeClient _pipeClient;
    private readonly CollectorCoordinator _collectors;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly ILogger<App> _logger;
    private bool _allowExit;

    public App(
        TrayIconService trayIcon,
        NamedPipeClient pipeClient,
        CollectorCoordinator collectors,
        ISessionDayMetrics dayMetrics,
        ILogger<App> logger)
    {
        InitializeComponent();
        _trayIcon   = trayIcon;
        _pipeClient = pipeClient;
        _collectors = collectors;
        _dayMetrics = dayMetrics;
        _logger     = logger;
        BootLog("App ctor completed");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            BootLog($"UnhandledException: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            BootLog($"UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };
    }

    private static void BootLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLogPath)!);
            File.AppendAllText(BootLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        BootLog("CreateWindow enter");

        // Track last completed session so ClockOut can land on //end once.
        var showEndAfterClockOut = false;

        _pipeClient.OnStatusReceived += status =>
        {
            if (status.State == MonitoringState.Stopped
                && status.Session?.ClockOutAt is not null
                && status.Session.ClockInAt is not null)
            {
                // Record the completed session HERE — synchronously, on this same
                // thread, before the OnStateReceived below can trigger navigation to
                // //end. ActiveSessionViewModel.RunLifecycleAsync also calls
                // RememberCompletedSession, but that resumes on a ConfigureAwait(false)
                // continuation racing against this exact navigation — this call is the
                // one guaranteed to land first.
                _dayMetrics.RememberCompletedSession(status.Session);
                showEndAfterClockOut = true;
            }
            if (status.State == MonitoringState.Active)
                showEndAfterClockOut = false;
        };

        _pipeClient.OnStateReceived += state =>
        {
            BootLog($"State received: {state}");
            _logger.LogInformation("Agent state: {State}", state);
            _trayIcon.UpdateState(state);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var route = state switch
                {
                    MonitoringState.Active     => "//active",
                    MonitoringState.Paused     => "//active", // On-break mode of ActiveSessionPage
                    MonitoringState.Stopped when showEndAfterClockOut => "//end",
                    MonitoringState.Stopped    => "//clockin",
                    MonitoringState.Unenrolled => "//connect",
                    MonitoringState.Locked     => "//connect",
                    _                          => "//clockin"
                };
                Shell.Current?.GoToAsync(route);
            });
        };

        _pipeClient.OnDisconnected += () =>
        {
            BootLog("IPC disconnected");
            _logger.LogWarning("IPC disconnected");
            _trayIcon.UpdateState(MonitoringState.Stopped);
            MainThread.BeginInvokeOnMainThread(() =>
                Shell.Current?.GoToAsync("//clockin"));
        };

        _ = _pipeClient.StartAsync(CancellationToken.None);

        var shell  = new ONEVO.Agent.TrayApp.Views.AppShell();
        var window = new Window(shell)
        {
            Title         = "OneXso WorkPulse",
            Width         = TrayLayoutMetrics.DefaultWindowWidth,
            Height        = TrayLayoutMetrics.DefaultWindowHeight,
            MinimumWidth  = TrayLayoutMetrics.MinimumWindowWidth,
            MinimumHeight = TrayLayoutMetrics.MinimumWindowHeight
        };

        window.Created    += (_, _) =>
        {
            BootLog("Window.Created");
            HookCloseToHide(window);
            try
            {
                _trayIcon.Initialize();
                BootLog("TrayIcon initialized");
            }
            catch (Exception ex)
            {
                BootLog($"TrayIcon init failed: {ex}");
                _logger.LogError(ex, "Tray icon init failed");
            }
        };

        window.Destroying += async (_, _) =>
        {
            BootLog("Window.Destroying");
            try
            {
                await _collectors.DisposeAsync();
                await _pipeClient.DisposeAsync();
                _trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                BootLog($"Shutdown error: {ex}");
            }
        };

        BootLog("CreateWindow exit");
        return window;
    }

    private static void SetUi(Action action)
    {
        try
        {
            if (MainThread.IsMainThread) action();
            else MainThread.BeginInvokeOnMainThread(action);
        }
        catch { }
    }

    private void HookCloseToHide(Window window)
    {
#if WINDOWS
        try
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
                && native.AppWindow is not null)
            {
                native.AppWindow.Closing += (_, e) =>
                {
                    if (_allowExit) return;
                    e.Cancel = true;
                    native.AppWindow.Hide();
                    BootLog("Window close cancelled — app continues in tray");
                };
            }
        }
        catch (Exception ex)
        {
            BootLog($"HookCloseToHide failed: {ex.Message}");
        }
#endif
    }

    public void RequestExit()
    {
        _allowExit = true;
        BootLog("Exit requested from tray");
        Quit();
    }
}
