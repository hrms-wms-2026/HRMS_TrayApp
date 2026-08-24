namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;

public sealed class SystemPowerEvents : ISystemPowerEvents
{
    public event PowerModeChangedEventHandler? PowerModeChanged
    {
        add => SystemEvents.PowerModeChanged += value;
        remove => SystemEvents.PowerModeChanged -= value;
    }
}
