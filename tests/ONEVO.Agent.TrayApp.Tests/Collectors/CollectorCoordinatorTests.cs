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
    public async Task Active_PolicyVersionChanges_RestartsCollectors()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);
        Assert.Equal(1, _collector.StartCount);
        Assert.Equal(0, _collector.StopCount);

        _collector.ResetSignals();
        var v2 = EnabledPolicy() with { Version = "v2" };
        _pipe.SimulatePolicy(v2);

        await _collector.WaitForStartAsync(Wait);

        Assert.Equal(2, _collector.StartCount);
        Assert.Equal(1, _collector.StopCount);
        Assert.Equal("v2", _collector.LastPolicy?.Version);
        Assert.True(_collector.IsRunning);
    }

    [Fact]
    public async Task PrepareForPauseAsync_StopsCollectors_EvenWhileStateActive()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        ICollectorLifecycleCoordinator lifecycle = _sut;
        await lifecycle.PrepareForPauseAsync(default);

        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task ResumeAfterRejectedPauseAsync_RestartsCollectors_WhenStateStillActive()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        ICollectorLifecycleCoordinator lifecycle = _sut;
        await lifecycle.PrepareForPauseAsync(default);
        Assert.False(_collector.IsRunning);

        _collector.ResetSignals();
        await lifecycle.ResumeAfterRejectedPauseAsync(default);
        await _collector.WaitForStartAsync(Wait);

        Assert.True(_collector.IsRunning);
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

    [Fact]
    public async Task Active_CollectorSelfStopped_SamePolicyVersionRePushed_RestartsIt()
    {
        // Outage-recovery regression: a collector (e.g. InactivityScreenshotCollector) can
        // self-stop internally between StartAsync and StopAsync — no StopAsync call, so the
        // coordinator's own _collectorsRunning bookkeeping never learns about it. If the backend
        // was unreachable past the policy TTL and then recovers with the exact same policy version
        // (unchanged toggles -> unchanged fingerprint, so PolicySyncService never broadcasts a new
        // version), the old versionChanged-only check would leave the collector dark forever. The
        // fix must restart it anyway.
        var selfStopping = new SelfStoppingAgentCollector();
        var pipe = new FakeNamedPipeClient();
        await using var sut = new CollectorCoordinator(
            NullLogger<CollectorCoordinator>.Instance, [selfStopping], pipe);

        pipe.SimulatePolicy(EnabledPolicy());
        pipe.SimulateState(MonitoringState.Active);
        await selfStopping.WaitForStartAsync(Wait);
        Assert.Equal(1, selfStopping.StartCount);

        // Simulate the internal self-stop: IsRunning flips to false with no StopAsync call.
        selfStopping.SimulateSelfStop();
        Assert.False(selfStopping.IsRunning);
        Assert.Equal(0, selfStopping.StopCount);
        selfStopping.ResetStartSignal();

        // Backend recovers and re-pushes the SAME policy version.
        pipe.SimulatePolicy(EnabledPolicy());

        await selfStopping.WaitForStartAsync(Wait);
        Assert.Equal(2, selfStopping.StartCount);
        Assert.True(selfStopping.IsRunning);
    }

    [Fact]
    public async Task Active_CollectorNeverEligible_SamePolicyVersionRePushed_DoesNotForceRestart()
    {
        // Guards against the naive version of the fix above: a collector that never started
        // because its OWN policy gate declined (e.g. a feature flag that stays off) must never be
        // mistaken for "stalled" — that would restart every collector on every state/policy event
        // for as long as the feature stays disabled, which is the normal, common steady state.
        var neverEligible = new SelfStoppingAgentCollector { RefuseToStart = true };
        var pipe = new FakeNamedPipeClient();
        await using var sut = new CollectorCoordinator(
            NullLogger<CollectorCoordinator>.Instance, [neverEligible], pipe);

        pipe.SimulatePolicy(EnabledPolicy());
        pipe.SimulateState(MonitoringState.Active);
        await Task.Delay(100);

        Assert.Equal(1, neverEligible.StartCount); // StartAsync was called...
        Assert.False(neverEligible.IsRunning);     // ...but the collector itself declined to run.

        pipe.SimulatePolicy(EnabledPolicy()); // same version, re-pushed
        await Task.Delay(100);

        Assert.Equal(1, neverEligible.StartCount); // must NOT have been restarted
        Assert.Equal(0, neverEligible.StopCount);
    }

    public async ValueTask DisposeAsync() => await _sut.DisposeAsync();
}

/// <summary>
/// Collector fake that can flip <see cref="IsRunning"/> to <c>false</c> on its own — mirroring
/// <see cref="InactivityScreenshotCollector"/>'s internal policy-staleness self-stop — without
/// going through <see cref="StopAsync"/>, so <see cref="StopCount"/> stays untouched. Declared here
/// (rather than tests/.../Fakes) so this file alone stays sufficient to compile the regression test.
/// </summary>
internal sealed class SelfStoppingAgentCollector : IAgentCollector
{
    private TaskCompletionSource _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => "SelfStopping";
    public bool IsRunning { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    /// <summary>When true, StartAsync mimics a collector whose own policy gate declines to run.</summary>
    public bool RefuseToStart { get; init; }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        StartCount++;
        IsRunning = !RefuseToStart;
        _startSignal.TrySetResult();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        IsRunning = false;
        StopCount++;
        return Task.CompletedTask;
    }

    /// <summary>Simulates an internal self-stop with no external StopAsync call.</summary>
    public void SimulateSelfStop() => IsRunning = false;

    public Task WaitForStartAsync(TimeSpan timeout) => _startSignal.Task.WaitAsync(timeout);

    public void ResetStartSignal() =>
        _startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
}
