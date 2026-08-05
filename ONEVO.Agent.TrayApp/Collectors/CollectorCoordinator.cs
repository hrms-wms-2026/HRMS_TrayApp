namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Starts/stops interactive collectors from monitoring state + policy.
/// Collectors never self-start. IPC loss stops all capture immediately.
/// </summary>
public sealed class CollectorCoordinator : IAsyncDisposable
{
    private readonly ILogger<CollectorCoordinator> _logger;
    private readonly IEnumerable<IAgentCollector> _collectors;
    private readonly NamedPipeClient _pipeClient;
    private readonly object _gate = new();

    private AgentPolicy? _policy;
    private MonitoringState _state = MonitoringState.Unenrolled;
    private bool _collectorsRunning;
    private CancellationTokenSource? _runCts;

    public CollectorCoordinator(
        ILogger<CollectorCoordinator> logger,
        IEnumerable<IAgentCollector> collectors,
        NamedPipeClient pipeClient)
    {
        _logger = logger;
        _collectors = collectors;
        _pipeClient = pipeClient;

        _pipeClient.OnStateReceived += OnStateReceived;
        _pipeClient.OnPolicyReceived += OnPolicyReceived;
        _pipeClient.OnDisconnected += OnDisconnected;
    }

    private void OnStateReceived(MonitoringState state)
    {
        lock (_gate)
        {
            _state = state;
        }
        _ = ReconcileAsync();
    }

    private void OnPolicyReceived(AgentPolicy policy)
    {
        lock (_gate)
        {
            _policy = policy;
        }
        _ = ReconcileAsync();
    }

    private void OnDisconnected()
    {
        _logger.LogWarning("IPC disconnected — stopping all collectors immediately");
        _ = StopAllAsync();
    }

    private async Task ReconcileAsync()
    {
        MonitoringState state;
        AgentPolicy? policy;
        lock (_gate)
        {
            state = _state;
            policy = _policy;
        }

        // Collect only when Active and we have a policy that enables features.
        var shouldRun = state == MonitoringState.Active && policy is not null;

        if (shouldRun)
            await StartAllAsync(policy!);
        else
            await StopAllAsync();
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
                await collector.StartAsync(policy, ct);
            }
            catch (Exception ex)
            {
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
            }
            catch (Exception ex)
            {
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
    }
}
