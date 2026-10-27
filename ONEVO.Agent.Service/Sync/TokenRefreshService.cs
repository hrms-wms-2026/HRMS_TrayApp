namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Refreshes the device access token before it expires. The access token issued by the
/// backend is short-lived (3600s). A transient failure (network, timeout, 5xx) keeps the
/// credentials and retries with capped exponential backoff + jitter — bounded retry, never
/// an infinite fast loop (§10). Only a backend rejection of the refresh token clears
/// credentials and locks the agent; that decision lives in <see cref="DeviceTokenRefresher"/>.
/// </summary>
public sealed class TokenRefreshService : BackgroundService
{
    // Access tokens live 3600s server-side; refresh well before that (§8.2 talks
    // about a 24h credential, but that does not match this token's real lifetime —
    // key the cadence off what the backend actually issues).
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(45);

    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ILogger<TokenRefreshService> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly DeviceTokenRefresher _refresher;

    public TokenRefreshService(
        ILogger<TokenRefreshService> logger,
        AgentStateMachine stateMachine,
        DeviceTokenRefresher refresher)
    {
        _logger = logger;
        _stateMachine = stateMachine;
        _refresher = refresher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        try
        {
            while (true)
            {
                await Task.Delay(NextDelay(consecutiveFailures, Random.Shared.NextDouble()), stoppingToken);

                if (_stateMachine.CurrentState is MonitoringState.Unenrolled or MonitoringState.Locked)
                {
                    consecutiveFailures = 0;
                    continue;
                }

                TokenRefreshOutcome outcome;
                try
                {
                    outcome = await _refresher.RefreshAsync(rejectedAccessToken: null, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // e.g. a DPAPI/IO failure writing the rotated token — must not stop the host.
                    _logger.LogWarning(ex, "Scheduled token refresh threw; treating as transient");
                    outcome = TokenRefreshOutcome.Transient;
                }

                if (outcome == TokenRefreshOutcome.Transient)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(
                        "Scheduled token refresh failed transiently (attempt {Attempt}); retrying with backoff",
                        consecutiveFailures);
                }
                else
                {
                    consecutiveFailures = 0;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 45 min after a success; after <paramref name="consecutiveFailures"/> transient failures,
    /// 15s doubling up to a 5 min cap, plus up to 20% jitter (<paramref name="jitterSample"/> in [0,1)).
    /// </summary>
    internal static TimeSpan NextDelay(int consecutiveFailures, double jitterSample)
    {
        if (consecutiveFailures <= 0)
            return RefreshInterval;

        var exponent = Math.Min(consecutiveFailures - 1, 10);
        var backoff = TimeSpan.FromTicks(Math.Min(
            InitialRetryDelay.Ticks * (1L << exponent),
            MaxRetryDelay.Ticks));
        return backoff + backoff * (0.2 * jitterSample);
    }
}
