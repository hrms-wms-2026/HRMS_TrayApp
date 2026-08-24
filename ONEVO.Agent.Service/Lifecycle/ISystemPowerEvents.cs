namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;

public interface ISystemPowerEvents
{
    event PowerModeChangedEventHandler? PowerModeChanged;
}
