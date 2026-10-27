namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Sends authenticated tray presence heartbeats every 30 seconds.
/// Credentials remain intact when a heartbeat cannot reach the backend; the next
/// tick retries. A token near expiry (e.g. after sleep/wake) is refreshed before the
/// heartbeat, and a 401 triggers one refresh + one retry so the web app's tray-presence
/// check is never blocked waiting for the 45-minute scheduled refresh.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    // While the agent-hub connection is live the backend treats the connection itself as
    // presence, so the HTTP heartbeat (a DB write each time) is only a slow keep-fresh.
    internal static readonly TimeSpan PushConnectedInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<HeartbeatService> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly DeviceTokenRefresher _refresher;
    private readonly PushChannelState _push;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        AgentStateMachine stateMachine,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        DeviceTokenRefresher refresher,
        PushChannelState? push = null)
    {
        _logger = logger;
        _stateMachine = stateMachine;
        _apiClient = apiClient;
        _credentials = credentials;
        _refresher = refresher;
        _push = push ?? new PushChannelState();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatService started with a {IntervalSeconds}s interval", HeartbeatInterval.TotalSeconds);

        try
        {
            await _push.RunAdaptiveLoopAsync(
                HeartbeatInterval, PushConnectedInterval, SafeSendHeartbeatAsync, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SafeSendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            await SendHeartbeatAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tray heartbeat failed — will retry next tick");
        }
    }

    internal async Task SendHeartbeatAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState is MonitoringState.Unenrolled or MonitoringState.Locked)
            return;

        // Wall-clock exp check: after sleep/wake the scheduled refresh may not have fired.
        // A transient failure here still lets the heartbeat try the current token.
        if (await _refresher.EnsureFreshAsync(ct) == TokenRefreshOutcome.Rejected)
            return;

        var accessToken = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        var result = await _apiClient.SendHeartbeatAsync(accessToken, ct);
        if (result == HeartbeatResult.Unauthorized)
        {
            _logger.LogInformation("Tray heartbeat returned 401; refreshing the device token and retrying once");
            if (await _refresher.RefreshAsync(accessToken, ct) != TokenRefreshOutcome.Succeeded)
                return;

            var refreshed = _credentials.ReadDeviceJwt();
            if (string.IsNullOrWhiteSpace(refreshed))
                return;

            result = await _apiClient.SendHeartbeatAsync(refreshed, ct);
        }

        if (result == HeartbeatResult.Accepted)
        {
            _logger.LogDebug("Tray heartbeat accepted");
            return;
        }

        // Do not clear credentials here: a network outage must not force a new
        // activation code. Only DeviceTokenRefresher decides a credential is dead.
        _logger.LogWarning("Tray heartbeat was rejected or unavailable ({Result}); retaining credentials for retry", result);
    }
}
