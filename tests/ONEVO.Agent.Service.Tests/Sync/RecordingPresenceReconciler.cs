namespace ONEVO.Agent.Service.Tests.Sync;

using ONEVO.Agent.Service.Sync;

public sealed class RecordingPresenceReconciler : IPresenceReconciler
{
    public List<string> Calls { get; } = [];

    private readonly Action? _onApplyActive;

    public RecordingPresenceReconciler(Action? onApplyActive = null) => _onApplyActive = onApplyActive;

    public bool ApplyPresenceActive(DateTimeOffset now)
    {
        Calls.Add("Active");
        _onApplyActive?.Invoke();
        return true;
    }

    public bool ApplyPresenceStopped(DateTimeOffset now)
    {
        Calls.Add("Stopped");
        return true;
    }
}
