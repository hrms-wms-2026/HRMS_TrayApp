namespace ONEVO.Agent.Service;

using ONEVO.Agent.Shared.Models;

public sealed class AgentStateMachine
{
    private MonitoringState _state = MonitoringState.Unenrolled;
    private readonly Lock _lock = new();

    public MonitoringState CurrentState
    {
        get { lock (_lock) return _state; }
    }

    public bool TryTransition(MonitoringState target, out MonitoringState previous)
    {
        lock (_lock)
        {
            previous = _state;
            if (!IsValidTransition(_state, target))
                return false;
            _state = target;
            return true;
        }
    }

    private static bool IsValidTransition(MonitoringState from, MonitoringState to) =>
        (from, to) switch
        {
            (MonitoringState.Unenrolled, MonitoringState.Stopped) => true,
            (MonitoringState.Stopped,    MonitoringState.Active)  => true,
            (MonitoringState.Active,     MonitoringState.Paused)  => true,
            (MonitoringState.Active,     MonitoringState.Stopped) => true,
            (MonitoringState.Paused,     MonitoringState.Active)  => true,
            (MonitoringState.Paused,     MonitoringState.Stopped) => true,
            (MonitoringState.Locked,     MonitoringState.Stopped) => true,
            (_,                          MonitoringState.Locked)  => true,
            _ => false
        };
}
