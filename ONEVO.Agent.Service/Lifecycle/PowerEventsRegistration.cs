namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Win32;

public static class PowerEventsRegistration
{
    /// <summary>
    /// As a Windows Service, power events come from the SCM through <see cref="PowerAwareServiceLifetime"/>;
    /// in a console / dev run (no SCM) they come from <see cref="SystemEvents"/>. Never both, or every
    /// transition would be handled twice.
    /// </summary>
    public static IServiceCollection AddPowerEvents(this IServiceCollection services, bool isWindowsService)
    {
        if (isWindowsService)
        {
            services.AddSingleton<ServicePowerEvents>();
            services.AddSingleton<ISystemPowerEvents>(sp => sp.GetRequiredService<ServicePowerEvents>());
            services.RemoveAll<IHostLifetime>();
            services.AddSingleton<IHostLifetime, PowerAwareServiceLifetime>();
        }
        else
        {
            services.AddSingleton<ISystemPowerEvents, SystemPowerEvents>();
        }
        services.AddHostedService<PowerModeIdleListener>();
        return services;
    }
}
