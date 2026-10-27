namespace ONEVO.Agent.Service.Sync;

public interface IPresenceReconciler
{
    /// <summary>Reconciles local state to Active for a session that started at
    /// <paramref name="clockedInAt"/>, with <paramref name="completedBreakMinutes"/> of breaks
    /// already completed today. No-op (returns true) if already Active/Paused.</summary>
    bool ApplyPresenceActive(DateTimeOffset clockedInAt, int completedBreakMinutes = 0);
    bool ApplyPresenceStopped(DateTimeOffset now);

    /// <summary>Reconciles local state to Paused when the backend reports an open break started
    /// through another channel (web today). No-op (returns true) if already Paused.</summary>
    bool ApplyPresenceBreakStarted(DateTimeOffset startedAt);

    /// <summary>Reconciles local state back to Active when the backend reports no open break. No-op
    /// (returns true) if already Active/not paused for a break.</summary>
    bool ApplyPresenceBreakEnded(DateTimeOffset now);

    /// <summary>Stores the company break allowance so the tray can lock Start Break
    /// and notify as soon as today's break time passes it.</summary>
    void ApplyBreakAllowance(bool canStartBreak, int? allowanceMinutes, int completedBreakMinutes);

    /// <summary>The backend reports today's attendance already closed. Restores that finished day
    /// into the local session if this process doesn't know it yet (e.g. after a restart).</summary>
    void ApplyCompletedDay(DateTimeOffset clockedInAt, DateTimeOffset clockedOutAt, int completedBreakMinutes);
}
