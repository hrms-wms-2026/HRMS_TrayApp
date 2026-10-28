namespace ONEVO.Agent.Service.Lifecycle;

using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

/// <summary>
/// The Windows Service lifetime with SCM power notifications turned on, forwarded to
/// <see cref="ServicePowerEvents"/> so sleep and hibernate reach <see cref="PowerModeIdleListener"/>.
/// </summary>
public sealed class PowerAwareServiceLifetime : WindowsServiceLifetime
{
    private readonly ServicePowerEvents _power;

    public PowerAwareServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        ServicePowerEvents power)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _power = power;
        // Must be set before the SCM starts the service; it cannot change afterwards.
        CanHandlePowerEvent = true;
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        HandlePowerEvent(powerStatus);
        return base.OnPowerEvent(powerStatus);
    }

    internal void HandlePowerEvent(PowerBroadcastStatus powerStatus) => _power.Handle(powerStatus);
}
