namespace ONEVO.Agent.Service.Sync;

public interface IPresenceReconciler
{
    bool ApplyPresenceActive(DateTimeOffset now);
    bool ApplyPresenceStopped(DateTimeOffset now);

    /// <summary>Reconciles local state to Paused when the backend reports an open break started
    /// through another channel (web today). No-op (returns true) if already Paused.</summary>
    bool ApplyPresenceBreakStarted(DateTimeOffset startedAt);

    /// <summary>Reconciles local state back to Active when the backend reports no open break. No-op
    /// (returns true) if already Active/not paused for a break.</summary>
    bool ApplyPresenceBreakEnded(DateTimeOffset now);
}
