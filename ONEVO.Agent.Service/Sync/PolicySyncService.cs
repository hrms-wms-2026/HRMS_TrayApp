namespace ONEVO.Agent.Service.Sync;

using System.Text.Json;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Fetches the effective monitoring policy from the backend and keeps <see cref="PolicyCache"/>
/// (and every connected Tray client) in sync with it. Modeled on <see cref="TokenRefreshService"/>:
/// a controlled cadence, no fast-retry storms, and a no-op whenever the Device JWT is missing.
///
/// Unlike TokenRefreshService, this does an immediate fetch as soon as a JWT is available — later
/// collectors (Task 6's inactivity workflow) read <see cref="PolicyCache.Current"/> directly and
/// should not run a whole hour on the local default before learning the tenant's real policy.
/// </summary>
public sealed class PolicySyncService : BackgroundService
{
    // Backend policy validity is 1 hour (see AgentApiRoutes.TrayPolicy). Refresh well before
    // that, not at exactly 1 hour: the PeriodicTimer here starts counting only after the prior
    // fetch completes, so a full-hour interval always lands strictly after the ValidUntil that
    // was stamped from the backend's earlier clock read — PolicyCache.Current would then be
    // guaranteed to degrade screenshot/inactivity-screenshot/camera flags to false for at least
    // the round-trip of every scheduled refresh (worse under HTTP retry/backoff). Mirrors
    // TokenRefreshService's own "refresh well before expiry" margin (45 min for a 60-min token).
    // Internal (not private) so PolicySyncServiceTests can assert the margin directly — see
    // RefreshInterval_LeavesMarginBeforeBackendPolicyValidity.
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan JwtPollInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger<PolicySyncService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly PolicyCache _policyCache;
    private readonly IIpcBroadcaster _broadcaster;

    public PolicySyncService(
        ILogger<PolicySyncService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        PolicyCache policyCache,
        IIpcBroadcaster broadcaster)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _policyCache = policyCache;
        _broadcaster = broadcaster;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jwt = await WaitForJwtAsync(stoppingToken);
        if (jwt is null) return; // cancelled while waiting for enrollment — service is stopping

        await RefreshOnceAsync(jwt, stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshOnceAsync(_credentials.ReadDeviceJwt(), stoppingToken);
        }
    }

    private async Task<string?> WaitForJwtAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var jwt = _credentials.ReadDeviceJwt();
            if (!string.IsNullOrWhiteSpace(jwt))
                return jwt;

            try
            {
                await Task.Delay(JwtPollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Fetches and applies one policy refresh. Public so tests can drive it directly without a
    /// stored Device JWT on disk (CredentialStore is DPAPI-backed and machine-scoped) — production
    /// callers only reach this through <see cref="ExecuteAsync"/>'s cadence above.
    /// </summary>
    public async Task RefreshOnceAsync(string? deviceJwt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceJwt))
        {
            _logger.LogDebug("Policy refresh skipped — no Device JWT");
            return;
        }

        var result = await _apiClient.GetEffectivePolicyAsync(deviceJwt, ct);
        if (!result.Success || result.Policy is null)
        {
            // Leave the cache exactly as-is (§PolicyCache.Current already degrades the
            // capture flags once its own ValidUntil passes) — no fast-retry loop here.
            _logger.LogDebug("Policy refresh failed ({ErrorCode}) — keeping last valid policy", result.ErrorCode);
            return;
        }

        var policy = result.Policy;
        if (policy.ValidUntil <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Backend returned an already-expired policy (version={Version}, validUntil={ValidUntil}) — rejecting",
                policy.Version, policy.ValidUntil);
            return;
        }

        var previousVersion = _policyCache.Current.Version;
        _policyCache.Set(policy);

        if (string.Equals(previousVersion, policy.Version, StringComparison.Ordinal))
        {
            _logger.LogDebug("Policy refreshed — version unchanged ({Version}), no broadcast", policy.Version);
            return;
        }

        await _broadcaster.BroadcastAsync(new IpcEnvelope
        {
            Type = IpcMessageTypes.PolicyPush,
            Payload = JsonSerializer.SerializeToElement(new PolicyPushPayload { Policy = policy })
        }, ct);

        _logger.LogInformation("Policy refreshed and broadcast (version={Version})", policy.Version);
    }
}
