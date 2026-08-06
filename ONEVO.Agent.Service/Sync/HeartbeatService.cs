namespace ONEVO.Agent.Service.Sync;

/// <summary>
/// Sends heartbeat every ~60s with safe health metrics (§12).
/// Never includes raw titles, PII, secrets, or image bytes.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly AgentStateMachine _stateMachine;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        AgentStateMachine stateMachine)
    {
        _logger       = logger;
        _stateMachine = stateMachine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatService started");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SendHeartbeatAsync(stoppingToken);
    }

    private Task SendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            // Phase 1 stub — full HTTP call wired when OnevoApiClient is built
            _logger.LogDebug("Heartbeat tick: state={State}", _stateMachine.CurrentState);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Heartbeat failed — will retry next tick");
        }
        return Task.CompletedTask;
    }
}
