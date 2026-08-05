namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly TrayIconService _trayIcon;
    private readonly NamedPipeClient _pipeClient;
    private readonly CollectorCoordinator _collectors;
    private readonly ILogger<App> _logger;

    public App(
        TrayIconService trayIcon,
        NamedPipeClient pipeClient,
        CollectorCoordinator collectors,
        ILogger<App> logger)
    {
        InitializeComponent();
        _trayIcon = trayIcon;
        _pipeClient = pipeClient;
        _collectors = collectors;
        _logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _trayIcon.Initialize();

        // CollectorCoordinator already subscribed to pipe events in its constructor.
        // Keep tray icon in sync with monitoring state.
        _pipeClient.OnStateReceived += state =>
        {
            _logger.LogInformation("Agent state received: {State}", state);
            _trayIcon.UpdateState(state);
        };

        _pipeClient.OnDisconnected += () =>
        {
            _logger.LogWarning("IPC disconnected — tray showing stopped state");
            _trayIcon.UpdateState(MonitoringState.Stopped);
        };

        _ = _pipeClient.StartAsync(CancellationToken.None);

        var window = new Window();
        window.Destroying += async (_, _) =>
        {
            await _collectors.DisposeAsync();
            await _pipeClient.DisposeAsync();
            _trayIcon.Dispose();
        };
        return window;
    }
}
