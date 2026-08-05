namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Services;

public partial class App : Application
{
    private readonly TrayIconService _trayIcon;
    private readonly NamedPipeClient _pipeClient;
    private readonly ILogger<App> _logger;

    public App(TrayIconService trayIcon, NamedPipeClient pipeClient, ILogger<App> logger)
    {
        InitializeComponent();
        _trayIcon = trayIcon;
        _pipeClient = pipeClient;
        _logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _trayIcon.Initialize();

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
            await _pipeClient.DisposeAsync();
            _trayIcon.Dispose();
        };
        return window;
    }
}
