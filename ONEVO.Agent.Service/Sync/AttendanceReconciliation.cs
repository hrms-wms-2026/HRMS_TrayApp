namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;

/// <summary>
/// Applies the backend's attendance state to local presence. Shared by the periodic poller and by
/// clock actions the backend reports as already done (AUD-009), so both reconcile the same way.
/// </summary>
public static class AttendanceReconciliation
{
    public static void Apply(IPresenceReconciler reconciler, AttendanceStatusResult status, DateTimeOffset now)
    {
        if (status.IsClockedIn)
        {
            reconciler.ApplyPresenceActive(status.ClockedInAtUtc ?? now, status.CompletedBreakMinutes);

            if (status.IsOnBreak)
                reconciler.ApplyPresenceBreakStarted(status.BreakStartedAtUtc ?? now);
            else
                reconciler.ApplyPresenceBreakEnded(now);
        }
        else
        {
            reconciler.ApplyPresenceStopped(now);

            if (status.DayClockedInAtUtc is { } dayIn && status.DayClockedOutAtUtc is { } dayOut)
                reconciler.ApplyCompletedDay(dayIn, dayOut, status.CompletedBreakMinutes);
        }

        reconciler.ApplyBreakAllowance(status.CanStartBreak, status.BreakAllowanceMinutes, status.CompletedBreakMinutes);
    }
}
