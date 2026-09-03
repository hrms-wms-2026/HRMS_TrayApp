namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;

/// <summary>
/// Polls the backend's real attendance state every 60s and reconciles local MonitoringState to
/// match — the single source of truth for every employee, regardless of whether they're allowed
/// to clock in from the tray. A tray-eligible employee's local button press still gets an
/// immediate local transition (see AgentWorker.ExecuteClockInAsync); this poller is what notices
/// clock-ins/outs made via any other channel (web today, biometric later) within its cadence.
/// Modeled directly on NotificationPollingService: same PeriodicTimer shape, same
/// public-for-tests PollOnceAsync, same swallow-and-retry-next-cycle failure handling.
/// </summary>
public sealed class AttendanceStatusSyncService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<AttendanceStatusSyncService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly IPresenceReconciler _reconciler;

    public AttendanceStatusSyncService(
        ILogger<AttendanceStatusSyncService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        IPresenceReconciler reconciler)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _reconciler = reconciler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var jwt = _credentials.ReadDeviceJwt();
                if (!string.IsNullOrWhiteSpace(jwt))
                    await PollOnceAsync(jwt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Attendance status poll failed — will retry next cycle");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Public so tests can drive one poll cycle directly without a stored Device JWT on disk.</summary>
    public async Task PollOnceAsync(string deviceJwt, CancellationToken ct)
    {
        var result = await _apiClient.GetAttendanceStatusAsync(deviceJwt, ct);
        if (!result.Success)
        {
            _logger.LogDebug("Attendance status fetch failed ({ErrorCode}) — keeping last-known local state", result.ErrorCode);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (result.IsClockedIn)
            _reconciler.ApplyPresenceActive(result.ClockedInAtUtc ?? now);
        else
            _reconciler.ApplyPresenceStopped(now);
    }
}
