namespace ONEVO.Agent.Service.Lifecycle;

using System.ServiceProcess;
using Microsoft.Win32;

/// <summary>
/// Power events delivered by the Service Control Manager to <see cref="PowerAwareServiceLifetime"/>.
/// <see cref="SystemEvents.PowerModeChanged"/> depends on a window message pump, which a Session-0
/// service does not reliably have, so when running as a service this replaces
/// <see cref="SystemPowerEvents"/> (AUD-014).
/// </summary>
public sealed class ServicePowerEvents : ISystemPowerEvents
{
    public event PowerModeChangedEventHandler? PowerModeChanged;

    /// <summary>Called on the SCM control thread; keep it cheap.</summary>
    internal void Handle(PowerBroadcastStatus status)
    {
        if (ToPowerMode(status) is { } mode)
            PowerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));
    }

    /// <summary>
    /// Suspend → Suspend. Both resume notifications → Resume: ResumeAutomatic always arrives,
    /// ResumeSuspend only after user input, and a second resume is a no-op downstream.
    /// </summary>
    internal static PowerModes? ToPowerMode(PowerBroadcastStatus status) => status switch
    {
        PowerBroadcastStatus.Suspend => PowerModes.Suspend,
        PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend => PowerModes.Resume,
        _ => null
    };
}
