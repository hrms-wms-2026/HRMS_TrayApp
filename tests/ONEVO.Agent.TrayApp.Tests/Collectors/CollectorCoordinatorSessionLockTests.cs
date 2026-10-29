namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using Microsoft.Win32;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

/// <summary>
/// AUD-014: the architecture requires interactive collectors to stop on screen lock and user switch.
/// Nothing listened for either, so keyboard/mouse/app-usage/screenshot collectors kept running behind
/// the lock screen. Collection resumes only when the session is unlocked and the Service still says
/// Active.
/// </summary>
public sealed class CollectorCoordinatorSessionLockTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static AgentPolicy Policy() => new()
    {
        Version = "v1",
        ActivitySignalEnabled = true,
        ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
    };

    private readonly FakeNamedPipeClient _pipe = new();
    private readonly FakeAgentCollector _collector = new();
    private readonly FakeInteractiveSessionState _session = new();

    private CollectorCoordinator Create() => new(
        NullLogger<CollectorCoordinator>.Instance,
        [_collector],
        _pipe,
        new NotificationService(NullLogger<NotificationService>.Instance),
        _session);

    [Fact]
    public async Task Lock_StopsRunningCollectors()
    {
        await using var sut = Create();
        _pipe.SimulatePolicy(Policy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _collector.ResetSignals();
        _session.Raise(locked: true);

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task Unlock_WhileActive_RestartsCollectors()
    {
        await using var sut = Create();
        _pipe.SimulatePolicy(Policy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);
        _session.Raise(locked: true);
        await _collector.WaitForStopAsync(Wait);

        _collector.ResetSignals();
        _session.Raise(locked: false);

        await _collector.WaitForStartAsync(Wait);
        Assert.True(_collector.IsRunning);
    }

    [Fact]
    public async Task ActiveWhileLocked_DoesNotStartUntilUnlocked()
    {
        await using var sut = Create();
        _session.Raise(locked: true);
        _pipe.SimulatePolicy(Policy());
        _pipe.SimulateState(MonitoringState.Active);

        await Task.Delay(300);
        Assert.False(_collector.IsRunning);

        _session.Raise(locked: false);
        await _collector.WaitForStartAsync(Wait);
        Assert.True(_collector.IsRunning);
    }

    [Fact]
    public async Task Unlock_WhileOnBreak_KeepsCollectorsStopped()
    {
        await using var sut = Create();
        _pipe.SimulatePolicy(Policy());
        _pipe.SimulateState(MonitoringState.Paused);
        _session.Raise(locked: true);

        _session.Raise(locked: false);

        await Task.Delay(300);
        Assert.False(_collector.IsRunning);
    }

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, true)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, true)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, true)]
    [InlineData(SessionSwitchReason.SessionLogoff, true)]
    [InlineData(SessionSwitchReason.SessionUnlock, false)]
    [InlineData(SessionSwitchReason.ConsoleConnect, false)]
    [InlineData(SessionSwitchReason.RemoteConnect, false)]
    [InlineData(SessionSwitchReason.SessionLogon, false)]
    public void SwitchReason_MapsToLockedState(SessionSwitchReason reason, bool locked)
    {
        Assert.Equal(locked, WindowsInteractiveSessionState.IsLockedAfter(reason));
    }

    [Fact]
    public void RemoteControlChange_IsNotALockTransition()
    {
        Assert.Null(WindowsInteractiveSessionState.IsLockedAfter(SessionSwitchReason.SessionRemoteControl));
    }

    private sealed class FakeInteractiveSessionState : IInteractiveSessionState
    {
        public bool IsLocked { get; private set; }
        public event Action<bool>? LockChanged;

        public void Raise(bool locked)
        {
            IsLocked = locked;
            LockChanged?.Invoke(locked);
        }
    }
}
