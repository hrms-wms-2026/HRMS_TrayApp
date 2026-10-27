namespace ONEVO.Agent.Service.Tests.Sync;

using ONEVO.Agent.Service.Sync;

public sealed class RecordingPresenceReconciler : IPresenceReconciler
{
    public List<string> Calls { get; } = [];

    private readonly Action? _onApplyActive;

    public RecordingPresenceReconciler(Action? onApplyActive = null) => _onApplyActive = onApplyActive;

    public DateTimeOffset? ActiveClockedInAt { get; private set; }
    public int? ActiveCompletedBreakMinutes { get; private set; }

    public bool ApplyPresenceActive(DateTimeOffset clockedInAt, int completedBreakMinutes = 0)
    {
        Calls.Add("Active");
        ActiveClockedInAt = clockedInAt;
        ActiveCompletedBreakMinutes = completedBreakMinutes;
        _onApplyActive?.Invoke();
        return true;
    }

    public bool ApplyPresenceStopped(DateTimeOffset now)
    {
        Calls.Add("Stopped");
        return true;
    }

    public bool ApplyPresenceBreakStarted(DateTimeOffset startedAt)
    {
        Calls.Add("BreakStarted");
        return true;
    }

    public bool ApplyPresenceBreakEnded(DateTimeOffset now)
    {
        Calls.Add("BreakEnded");
        return true;
    }

    public void ApplyBreakAllowance(bool canStartBreak, int? allowanceMinutes, int completedBreakMinutes)
    {
        Calls.Add("BreakAllowance");
    }

    public void ApplyCompletedDay(DateTimeOffset clockedInAt, DateTimeOffset clockedOutAt, int completedBreakMinutes)
    {
        Calls.Add("CompletedDay");
    }
}
