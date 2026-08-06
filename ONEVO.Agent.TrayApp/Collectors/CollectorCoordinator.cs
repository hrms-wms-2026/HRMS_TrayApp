namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Starts/stops interactive collectors from monitoring state + policy.
/// Collectors never self-start. IPC loss stops all capture immediately.
/// </summary>
public sealed class CollectorCoordinator : IAsyncDisposable
{
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    /// <summary>
    /// Used when Service sends Active before PolicyPush arrives (or policy message is missed).
    /// Activity is on; other collectors stay off until real policy arrives.
    /// </summary>
    private static readonly AgentPolicy LocalDefaultPolicy = new()
    {
        Version = "tray-local-default",
        ActivitySignalEnabled = true,
        AppUsageEnabled = false,
        ScreenshotEnabled = false,
        CameraVerificationEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddDays(1)
    };

    private readonly ILogger<CollectorCoordinator> _logger;
    private readonly IEnumerable<IAgentCollector> _collectors;
    private readonly INamedPipeClient _pipeClient;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    private AgentPolicy? _policy;
    private MonitoringState _state = MonitoringState.Unenrolled;
    private bool _collectorsRunning;
    private CancellationTokenSource? _runCts;

    public CollectorCoordinator(
        ILogger<CollectorCoordinator> logger,
        IEnumerable<IAgentCollector> collectors,
        INamedPipeClient pipeClient)
    {
        _logger = logger;
        _collectors = collectors;
        _pipeClient = pipeClient;

        _pipeClient.OnStateReceived += OnStateReceived;
        _pipeClient.OnPolicyReceived += OnPolicyReceived;
        _pipeClient.OnDisconnected += OnDisconnected;
    }

    private static void BootLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLogPath)!);
            File.AppendAllText(BootLogPath, $"{DateTimeOffset.Now:O} [Coordinator] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    private void OnStateReceived(MonitoringState state)
    {
        lock (_gate)
        {
            _state = state;
        }
        BootLog($"State={state}");
        _ = ReconcileAsync();
    }

    private void OnPolicyReceived(AgentPolicy policy)
    {
        lock (_gate)
        {
            _policy = policy;
        }
        BootLog($"Policy received Version={policy.Version} Activity={policy.ActivitySignalEnabled}");
        _ = ReconcileAsync();
    }

    private void OnDisconnected()
    {
        BootLog("IPC disconnected — stop collectors");
        _logger.LogWarning("IPC disconnected — stopping all collectors immediately");
        _ = StopAllAsync();
    }

    private async Task ReconcileAsync()
    {
        await _reconcileLock.WaitAsync();
        try
        {
            MonitoringState state;
            AgentPolicy? policy;
            lock (_gate)
            {
                state = _state;
                policy = _policy;
            }

            // Phase-1: if Active but policy not yet received, use local default
            // so keyboard/mouse capture can start immediately after auth.
            var effective = policy
                ?? (state == MonitoringState.Active ? LocalDefaultPolicy : null);

            var shouldRun = state == MonitoringState.Active
                && effective is not null
                && effective.ActivitySignalEnabled;

            BootLog($"Reconcile state={state} hasPolicy={policy is not null} shouldRun={shouldRun}");

            if (shouldRun)
                await StartAllAsync(effective!);
            else
                await StopAllAsync();
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task StartAllAsync(AgentPolicy policy)
    {
        lock (_gate)
        {
            if (_collectorsRunning)
                return;
            _collectorsRunning = true;
            _runCts = new CancellationTokenSource();
        }

        var ct = _runCts!.Token;
        foreach (var collector in _collectors)
        {
            try
            {
                BootLog($"Starting collector {collector.Name}");
                await collector.StartAsync(policy, ct);
                BootLog($"Started collector {collector.Name}");
                _logger.LogInformation("Collector started: {Name}", collector.Name);
            }
            catch (Exception ex)
            {
                BootLog($"Start failed {collector.Name}: {ex}");
                _logger.LogError(ex, "Failed to start collector {Name}", collector.Name);
            }
        }
    }

    private async Task StopAllAsync()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_collectorsRunning)
                return;
            _collectorsRunning = false;
            cts = _runCts;
            _runCts = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        foreach (var collector in _collectors)
        {
            try
            {
                await collector.StopAsync(CancellationToken.None);
                BootLog($"Stopped collector {collector.Name}");
            }
            catch (Exception ex)
            {
                BootLog($"Stop failed {collector.Name}: {ex.Message}");
                _logger.LogError(ex, "Failed to stop collector {Name}", collector.Name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pipeClient.OnStateReceived -= OnStateReceived;
        _pipeClient.OnPolicyReceived -= OnPolicyReceived;
        _pipeClient.OnDisconnected -= OnDisconnected;
        await StopAllAsync();
        _reconcileLock.Dispose();
    }
}
