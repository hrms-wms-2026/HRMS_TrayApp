using System.ServiceProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

/// <summary>
/// AUD-014 part 2: sleep / hibernate must reach presence idle when running as a Session-0 service,
/// where SystemEvents.PowerModeChanged is not reliable — via SCM power events on the host lifetime.
/// </summary>
public sealed class ServicePowerEventsTests
{
    private sealed class FakeBroadcaster : IIpcBroadcaster
    {
        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PowerAwareServiceLifetime CreateLifetime(ServicePowerEvents power) => new(
        new HostingEnvironment { ApplicationName = "test", EnvironmentName = "Production" },
        new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
        NullLoggerFactory.Instance,
        Options.Create(new HostOptions()),
        Options.Create(new WindowsServiceLifetimeOptions()),
        power);

    [Theory]
    [InlineData(PowerBroadcastStatus.Suspend, PowerModes.Suspend)]
    [InlineData(PowerBroadcastStatus.ResumeAutomatic, PowerModes.Resume)]
    [InlineData(PowerBroadcastStatus.ResumeSuspend, PowerModes.Resume)]
    public void ToPowerMode_MapsSuspendAndResume(PowerBroadcastStatus status, PowerModes expected) =>
        Assert.Equal(expected, ServicePowerEvents.ToPowerMode(status));

    [Theory]
    [InlineData(PowerBroadcastStatus.QuerySuspend)]
    [InlineData(PowerBroadcastStatus.QuerySuspendFailed)]
    [InlineData(PowerBroadcastStatus.BatteryLow)]
    [InlineData(PowerBroadcastStatus.PowerStatusChange)]
    [InlineData(PowerBroadcastStatus.OemEvent)]
    [InlineData(PowerBroadcastStatus.ResumeCritical)]
    public void ToPowerMode_IgnoresOtherNotifications(PowerBroadcastStatus status) =>
        Assert.Null(ServicePowerEvents.ToPowerMode(status));

    [Fact]
    public void Handle_RaisesPowerModeChanged()
    {
        var power = new ServicePowerEvents();
        var raised = new List<PowerModes>();
        power.PowerModeChanged += (_, e) => raised.Add(e.Mode);

        power.Handle(PowerBroadcastStatus.Suspend);
        power.Handle(PowerBroadcastStatus.PowerStatusChange);
        power.Handle(PowerBroadcastStatus.ResumeAutomatic);

        Assert.Equal([PowerModes.Suspend, PowerModes.Resume], raised);
    }

    [Fact]
    public void Lifetime_AsksScmForPowerEvents()
    {
        using var lifetime = CreateLifetime(new ServicePowerEvents());
        Assert.True(lifetime.CanHandlePowerEvent);
    }

    [Fact]
    public async Task SuspendFromScm_OnActiveSession_StartsIdle_AndResumeEndsIt()
    {
        var session = new PresenceSession();
        session.ClockIn(DateTimeOffset.UtcNow.AddHours(-1));
        var power = new ServicePowerEvents();
        var listener = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, new FakeBroadcaster(), new AgentStateMachine());
        await listener.StartAsync(CancellationToken.None);
        using var lifetime = CreateLifetime(power);

        lifetime.HandlePowerEvent(PowerBroadcastStatus.Suspend);
        Assert.True(session.Snapshot(DateTimeOffset.UtcNow).IsIdle);

        await Task.Delay(20);
        lifetime.HandlePowerEvent(PowerBroadcastStatus.ResumeAutomatic);
        lifetime.HandlePowerEvent(PowerBroadcastStatus.ResumeSuspend);
        var snap = session.Snapshot(DateTimeOffset.UtcNow);
        Assert.False(snap.IsIdle);
        Assert.True(snap.AccumulatedIdle > TimeSpan.Zero);

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddPowerEvents_AsWindowsService_UsesScmLifetimeAndOneSharedEventSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostLifetime, WindowsServiceLifetime>(); // what UseWindowsService registers

        services.AddPowerEvents(isWindowsService: true);

        Assert.Equal(typeof(PowerAwareServiceLifetime),
            Assert.Single(services, d => d.ServiceType == typeof(IHostLifetime)).ImplementationType);
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(SystemPowerEvents));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(PowerModeIdleListener));

        using var sp = services.AddLogging().BuildServiceProvider();
        Assert.Same(sp.GetRequiredService<ServicePowerEvents>(), sp.GetRequiredService<ISystemPowerEvents>());
    }

    [Fact]
    public void AddPowerEvents_OutsideScm_KeepsSystemEventsAndDefaultLifetime()
    {
        var services = new ServiceCollection();

        services.AddPowerEvents(isWindowsService: false);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostLifetime));
        Assert.Equal(typeof(SystemPowerEvents),
            Assert.Single(services, d => d.ServiceType == typeof(ISystemPowerEvents)).ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ServicePowerEvents));
    }
}
