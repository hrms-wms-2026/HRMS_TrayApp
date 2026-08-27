namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Sends authenticated tray presence heartbeats every 30 seconds.
/// Credentials remain intact when a heartbeat cannot reach the backend; the next
/// tick retries and the token-refresh service remains responsible for scheduled
/// access-token rotation and unrecoverable refresh failures.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<HeartbeatService> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        AgentStateMachine stateMachine,
        OnevoApiClient apiClient,
        CredentialStore credentials)
    {
        _logger = logger;
        _stateMachine = stateMachine;
        _apiClient = apiClient;
        _credentials = credentials;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatService started with a {IntervalSeconds}s interval", HeartbeatInterval.TotalSeconds);
        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SendHeartbeatAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState is MonitoringState.Unenrolled or MonitoringState.Locked)
            return;

        var accessToken = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        var accepted = await _apiClient.SendHeartbeatAsync(accessToken, ct);
        if (accepted)
        {
            _logger.LogDebug("Tray heartbeat accepted");
            return;
        }

        // Do not clear credentials here: a network outage must not force a new
        // activation code. TokenRefreshService handles scheduled refresh and the
        // next heartbeat tick retries with the retained credential.
        _logger.LogWarning("Tray heartbeat was rejected or unavailable; retaining credentials for retry");
    }
}
