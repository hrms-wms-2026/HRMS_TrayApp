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
    private readonly IPreferencesStore _preferences;
    private readonly ILogger<App> _logger;
    private bool _allowExit;

    public App(
        TrayIconService trayIcon,
        NamedPipeClient pipeClient,
        CollectorCoordinator collectors,
        ISessionDayMetrics dayMetrics,
        IPreferencesStore preferences,
        ILogger<App> logger)
    {
        InitializeComponent();
        _trayIcon     = trayIcon;
        _pipeClient   = pipeClient;
        _collectors   = collectors;
        _dayMetrics   = dayMetrics;
        _preferences  = preferences;
        _logger       = logger;
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
                    MonitoringState.Stopped    => WorkLocationFlow.RouteWhenStopped(
                        _preferences, _pipeClient.LastKnownPolicy?.TrayClockInEnabled ?? false),
                    MonitoringState.Unenrolled => "//connect",
                    MonitoringState.Locked     => "//connect",
                    _                          => WorkLocationFlow.RouteWhenStopped(
                        _preferences, _pipeClient.LastKnownPolicy?.TrayClockInEnabled ?? false)
                };
                if (!string.IsNullOrEmpty(route))
                    Shell.Current?.GoToAsync(route);
            });
        };

        _pipeClient.OnDisconnected += () =>
        {
            BootLog("IPC disconnected");
            _logger.LogWarning("IPC disconnected");
            _trayIcon.UpdateState(MonitoringState.Stopped);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var route = WorkLocationFlow.RouteWhenStopped(
                    _preferences, _pipeClient.LastKnownPolicy?.TrayClockInEnabled ?? false);
                if (!string.IsNullOrEmpty(route))
                    Shell.Current?.GoToAsync(route);
            });
        };

        _ = _pipeClient.StartAsync(CancellationToken.None);

        var shell  = new ONEVO.Agent.TrayApp.Views.AppShell();
        var window = new Window(shell)
        {
            Title         = "OneXso WorkPulse",
            Width         = TrayLayoutMetrics.DefaultWindowWidth,
            Height        = TrayLayoutMetrics.DefaultWindowHeight,
            MinimumWidth  = TrayLayoutMetrics.MinimumWindowWidth,
            MinimumHeight = TrayLayoutMetrics.MinimumWindowHeight,
            TitleBar      = new TitleBar
            {
                Title           = "OneXso WorkPulse",
                Icon            = "onexso_x_mark.png",
                BackgroundColor = Colors.White,
                ForegroundColor = Color.FromArgb("#0F1B2D")
            }
        };

        window.Created    += (_, _) =>
        {
            BootLog("Window.Created");
            ApplyLightCaption(window);
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

    private static void ApplyLightCaption(Window window)
    {
#if WINDOWS
        try
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native
                || native.AppWindow?.TitleBar is null)
                return;

            var white    = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
            var text     = global::Windows.UI.Color.FromArgb(255, 15, 27, 45);
            var hover    = global::Windows.UI.Color.FromArgb(255, 241, 245, 249);
            var pressed  = global::Windows.UI.Color.FromArgb(255, 226, 232, 240);
            var inactive = global::Windows.UI.Color.FromArgb(255, 107, 122, 142);

            var titleBar = native.AppWindow.TitleBar;
            titleBar.BackgroundColor                 = white;
            titleBar.ForegroundColor                 = text;
            titleBar.InactiveBackgroundColor         = white;
            titleBar.InactiveForegroundColor         = inactive;
            titleBar.ButtonBackgroundColor           = white;
            titleBar.ButtonForegroundColor           = text;
            titleBar.ButtonHoverBackgroundColor      = hover;
            titleBar.ButtonHoverForegroundColor      = text;
            titleBar.ButtonPressedBackgroundColor    = pressed;
            titleBar.ButtonPressedForegroundColor    = text;
            titleBar.ButtonInactiveBackgroundColor   = white;
            titleBar.ButtonInactiveForegroundColor   = inactive;
        }
        catch (Exception ex)
        {
            BootLog($"ApplyLightCaption failed: {ex.Message}");
        }
#endif
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
