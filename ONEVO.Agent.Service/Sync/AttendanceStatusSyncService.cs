namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

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

    // While the backend can push (AttendanceChanged on clock-in/out/break), this is only a safety net.
    internal static readonly TimeSpan PushFallbackInterval = TimeSpan.FromMinutes(15);

    private readonly ILogger<AttendanceStatusSyncService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly IPresenceReconciler _reconciler;
    private readonly PushChannelState _push;
    private readonly AgentStateMachine? _stateMachine;

    public AttendanceStatusSyncService(
        ILogger<AttendanceStatusSyncService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        IPresenceReconciler reconciler,
        PushChannelState? push = null,
        AgentStateMachine? stateMachine = null)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _reconciler = reconciler;
        _push = push ?? new PushChannelState();
        _stateMachine = stateMachine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // This poll is also what makes the backend evaluate "break allowance exceeded",
            // so keep it fast while the employee is on break.
            await _push.RunAdaptiveLoopAsync(
                PollInterval, PushFallbackInterval, SafePollAsync, stoppingToken,
                forceFast: () => _stateMachine?.CurrentState == MonitoringState.Paused);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Never throws except on shutdown — also called from push handlers.</summary>
    public async Task SafePollAsync(CancellationToken ct)
    {
        try
        {
            var jwt = _credentials.ReadDeviceJwt();
            if (!string.IsNullOrWhiteSpace(jwt))
                await PollOnceAsync(jwt, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Attendance status poll failed — will retry next cycle");
        }
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
        {
            _reconciler.ApplyPresenceActive(result.ClockedInAtUtc ?? now);

            if (result.IsOnBreak)
                _reconciler.ApplyPresenceBreakStarted(result.BreakStartedAtUtc ?? now);
            else
                _reconciler.ApplyPresenceBreakEnded(now);
        }
        else
        {
            _reconciler.ApplyPresenceStopped(now);

            if (result.DayClockedInAtUtc is { } dayIn && result.DayClockedOutAtUtc is { } dayOut)
                _reconciler.ApplyCompletedDay(dayIn, dayOut, result.CompletedBreakMinutes);
        }

        _reconciler.ApplyBreakAllowance(result.CanStartBreak, result.BreakAllowanceMinutes, result.CompletedBreakMinutes);
    }
}
