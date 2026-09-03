namespace ONEVO.Agent.Service.Sync;

public interface IPresenceReconciler
{
    bool ApplyPresenceActive(DateTimeOffset now);
    bool ApplyPresenceStopped(DateTimeOffset now);
}
