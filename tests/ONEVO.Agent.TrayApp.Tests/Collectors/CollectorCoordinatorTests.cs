namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class CollectorCoordinatorTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static AgentPolicy EnabledPolicy(bool activityEnabled = true) => new()
    {
        Version                   = "v1",
        ActivitySignalEnabled     = activityEnabled,
        AppUsageEnabled           = false,
        ScreenshotEnabled         = false,
        CameraVerificationEnabled = false,
        ValidUntil                = DateTimeOffset.UtcNow.AddHours(1)
    };

    private readonly FakeNamedPipeClient _pipe      = new();
    private readonly FakeAgentCollector  _collector = new();
    private readonly CollectorCoordinator _sut;

    public CollectorCoordinatorTests()
    {
        _sut = new CollectorCoordinator(
            NullLogger<CollectorCoordinator>.Instance,
            [_collector],
            _pipe);
    }

    [Fact]
    public async Task Active_WithEnabledPolicy_StartsCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);

        await _collector.WaitForStartAsync(Wait);

        Assert.True(_collector.IsRunning);
        Assert.Equal("v1", _collector.LastPolicy?.Version);
    }

    [Fact]
    public async Task Active_PolicyDisablesActivity_DoesNotStartCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy(activityEnabled: false));
        _pipe.SimulateState(MonitoringState.Active);

        await Task.Delay(100);

        Assert.False(_collector.IsRunning);
        Assert.Equal(0, _collector.StartCount);
    }

    [Fact]
    public async Task IpcDisconnect_StopsAllCollectors_Immediately()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);
        Assert.True(_collector.IsRunning);

        _pipe.SimulateDisconnect();

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task State_Paused_StopsRunningCollectors()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _pipe.SimulateState(MonitoringState.Paused);

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task State_ActiveThenPausedThenActive_RestartsCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _collector.ResetSignals();
        _pipe.SimulateState(MonitoringState.Paused);
        await _collector.WaitForStopAsync(Wait);

        _collector.ResetSignals();
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        Assert.True(_collector.IsRunning);
        Assert.Equal(2, _collector.StartCount);
    }

    [Fact]
    public async Task StartAll_IsIdempotent_WhenCalledTwice()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _pipe.SimulatePolicy(EnabledPolicy());
        await Task.Delay(100);

        Assert.Equal(1, _collector.StartCount);
    }

    [Fact]
    public async Task State_Locked_StopsCollectors()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _pipe.SimulateState(MonitoringState.Locked);

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    public async ValueTask DisposeAsync() => await _sut.DisposeAsync();
}
