namespace ONEVO.Agent.Service.Security;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Shared.Models;

public enum TokenRefreshOutcome
{
    /// <summary>The stored access token is usable — refreshed now, by a concurrent caller, or not yet near expiry.</summary>
    Succeeded,
    /// <summary>Network error, timeout, 5xx, or any non-401 failure. Credentials are retained; retry later.</summary>
    Transient,
    /// <summary>The backend rejected the refresh token (401). Credentials were cleared and the agent is Locked.</summary>
    Rejected,
    /// <summary>No device identity or refresh token is stored — nothing to refresh.</summary>
    NoCredential,
}

/// <summary>
/// The single path through which the Service rotates its device access token. The backend
/// rotates the refresh token on every call, so two overlapping refreshes with the same old
/// refresh token would make the loser look revoked — every caller is therefore serialized,
/// and a caller whose rejected access token was already replaced reuses that result instead
/// of calling the backend again.
///
/// Only a 401 from the refresh endpoint is fatal (backend contract: invalid/expired refresh
/// token, inactive device, fingerprint mismatch). Everything else is transient and must never
/// cost the employee a new activation code.
/// </summary>
public sealed class DeviceTokenRefresher
{
    // Refresh this far ahead of exp so a heartbeat never goes out with a token that is about
    // to lapse — and so a token that expired while the machine slept is refreshed first.
    internal static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly ILogger<DeviceTokenRefresher> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly DeviceIdentityStore _deviceIdentityStore;
    private readonly PendingLegalChallengeStore _pendingLegalChallenge;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceTokenRefresher(
        ILogger<DeviceTokenRefresher> logger,
        AgentStateMachine stateMachine,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        DeviceIdentityStore deviceIdentityStore,
        PendingLegalChallengeStore pendingLegalChallenge,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _stateMachine = stateMachine;
        _apiClient = apiClient;
        _credentials = credentials;
        _deviceIdentityStore = deviceIdentityStore;
        _pendingLegalChallenge = pendingLegalChallenge;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Refreshes unconditionally, unless <paramref name="rejectedAccessToken"/> was already
    /// replaced by a concurrent caller — then the new token is used as-is.
    /// </summary>
    public Task<TokenRefreshOutcome> RefreshAsync(string? rejectedAccessToken, CancellationToken ct) =>
        RunExclusiveAsync(
            () => rejectedAccessToken is not null && IsReplaced(_credentials.ReadDeviceJwt(), rejectedAccessToken),
            ct);

    /// <summary>Refreshes only when the stored access token is missing or within <see cref="ExpirySkew"/> of exp.</summary>
    public Task<TokenRefreshOutcome> EnsureFreshAsync(CancellationToken ct) =>
        RunExclusiveAsync(() => !NeedsRefresh(_credentials.ReadDeviceJwt()), ct);

    private async Task<TokenRefreshOutcome> RunExclusiveAsync(Func<bool> alreadyFresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Evaluated inside the gate so a refresh that just completed is visible.
            if (alreadyFresh())
                return TokenRefreshOutcome.Succeeded;

            return await RefreshCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TokenRefreshOutcome> RefreshCoreAsync()
    {
        var identity = _deviceIdentityStore.Load();
        var refreshToken = _credentials.ReadRefreshToken();
        if (identity is null || string.IsNullOrWhiteSpace(refreshToken))
            return TokenRefreshOutcome.NoCredential;

        // Deliberately not the caller's token: once the backend has rotated the refresh token,
        // abandoning the response (e.g. on shutdown) would leave only the old, now-invalid one
        // on disk. The OnevoApi client's own timeout bounds this call.
        var result = await _apiClient.RefreshTokenAsync(refreshToken, identity.DeviceFingerprint, CancellationToken.None);
        if (result.Success && result.Auth is not null)
        {
            // Refresh token first — a crash between the writes must not lose the rotated one.
            _credentials.StoreRefreshToken(result.Auth.RefreshToken);
            _credentials.StoreDeviceJwt(result.Auth.AccessToken);
            _pendingLegalChallenge.Set(result.Auth.LegalChallenge, result.Auth.LegalCsrfToken);
            _logger.LogDebug("Device token refreshed");
            return TokenRefreshOutcome.Succeeded;
        }

        if (result.ErrorCode == "UNAUTHORIZED")
        {
            _logger.LogWarning("Refresh token was rejected by the backend — clearing credentials, moving to Locked");
            _credentials.ClearDeviceJwt();
            _credentials.ClearRefreshToken();
            _deviceIdentityStore.Clear();
            _stateMachine.TryTransition(MonitoringState.Locked, out _);
            return TokenRefreshOutcome.Rejected;
        }

        _logger.LogWarning("Token refresh failed transiently ({ErrorCode}); retaining credentials", result.ErrorCode);
        return TokenRefreshOutcome.Transient;
    }

    private static bool IsReplaced(string? current, string rejected) =>
        !string.IsNullOrWhiteSpace(current) && !string.Equals(current, rejected, StringComparison.Ordinal);

    private bool NeedsRefresh(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return true;

        // No readable exp: rely on the 401 path rather than refreshing on every heartbeat.
        var expiry = JwtClaimsReader.ReadExpiry(accessToken);
        return expiry is not null && expiry.Value - ExpirySkew <= _time.GetUtcNow();
    }
}
