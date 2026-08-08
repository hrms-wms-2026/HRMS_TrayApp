namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Refreshes the device access token before it expires. The access token issued
/// by the backend is short-lived (3600s) — this is a single controlled refresh
/// cycle, never an infinite fast retry (§10). A refresh failure means the stored
/// credential is dead; it clears local state and lets the agent fall back to
/// Unenrolled rather than keep retrying against a revoked/invalid token.
/// </summary>
public sealed class TokenRefreshService : BackgroundService
{
    // Access tokens live 3600s server-side; refresh well before that (§8.2 talks
    // about a 24h credential, but that does not match this token's real lifetime —
    // key the cadence off what the backend actually issues).
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(45);

    private readonly ILogger<TokenRefreshService> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly DeviceIdentityStore _deviceIdentityStore;

    public TokenRefreshService(
        ILogger<TokenRefreshService> logger,
        AgentStateMachine stateMachine,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        DeviceIdentityStore deviceIdentityStore)
    {
        _logger = logger;
        _stateMachine = stateMachine;
        _apiClient = apiClient;
        _credentials = credentials;
        _deviceIdentityStore = deviceIdentityStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_stateMachine.CurrentState is MonitoringState.Unenrolled or MonitoringState.Locked)
                continue;

            await RefreshOnceAsync(stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        var identity = _deviceIdentityStore.Load();
        var refreshToken = _credentials.ReadRefreshToken();
        if (identity is null || string.IsNullOrWhiteSpace(refreshToken))
            return;

        var result = await _apiClient.RefreshTokenAsync(refreshToken, identity.DeviceFingerprint, ct);
        if (result.Success && result.Auth is not null)
        {
            _credentials.StoreRefreshToken(result.Auth.RefreshToken);
            _credentials.StoreDeviceJwt(result.Auth.AccessToken);
            _logger.LogDebug("Device token refreshed on schedule");
            return;
        }

        // Single controlled retry, not a loop — if it fails again the credential
        // is treated as dead until the employee re-enrolls (§14).
        _logger.LogWarning("Scheduled token refresh failed ({ErrorCode}); retrying once", result.ErrorCode);
        var retry = await _apiClient.RefreshTokenAsync(refreshToken, identity.DeviceFingerprint, ct);
        if (retry.Success && retry.Auth is not null)
        {
            _credentials.StoreRefreshToken(retry.Auth.RefreshToken);
            _credentials.StoreDeviceJwt(retry.Auth.AccessToken);
            return;
        }

        _logger.LogWarning(
            "Token refresh failed twice ({ErrorCode}) — clearing credentials, moving to Locked", retry.ErrorCode);
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
        _deviceIdentityStore.Clear();
        _stateMachine.TryTransition(MonitoringState.Locked, out _);
    }
}
