using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

public sealed class PowerModeIdleListenerTests
{
    private sealed class FakePower : ISystemPowerEvents
    {
        public event PowerModeChangedEventHandler? PowerModeChanged;
        public void Raise(PowerModes mode) =>
            PowerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));
    }

    private sealed class FakeBroadcaster : IIpcBroadcaster
    {
        public int Broadcasts { get; private set; }
        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
        {
            Broadcasts++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SuspendThenResume_OnActiveSession_AccumulatesIdle()
    {
        var session = new PresenceSession();
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        session.ClockIn(t0);

        var power = new FakePower();
        var broadcast = new FakeBroadcaster();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, broadcast, new AgentStateMachine());

        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);
        Assert.True(session.Snapshot(DateTimeOffset.UtcNow).IsIdle);

        await Task.Delay(50);
        power.Raise(PowerModes.Resume);
        var snap = session.Snapshot(DateTimeOffset.UtcNow);
        Assert.False(snap.IsIdle);
        Assert.True(snap.AccumulatedIdle > TimeSpan.Zero);
        Assert.True(broadcast.Broadcasts >= 2);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Suspend_WhileAlreadyIdle_DoesNotResetStart()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var idleStart = t0.AddMinutes(10);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, idleStart);

        var power = new FakePower();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, new FakeBroadcaster(), new AgentStateMachine());
        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);

        var snap = session.Snapshot(idleStart.AddMinutes(1));
        Assert.Equal(idleStart, snap.CurrentIdleStartedAt);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Suspend_WithNoActiveSession_IsNoOp()
    {
        var session = new PresenceSession();
        var power = new FakePower();
        var broadcast = new FakeBroadcaster();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, broadcast, new AgentStateMachine());
        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);
        power.Raise(PowerModes.Resume);
        Assert.Equal(0, broadcast.Broadcasts);
        await sut.StopAsync(CancellationToken.None);
    }
}
