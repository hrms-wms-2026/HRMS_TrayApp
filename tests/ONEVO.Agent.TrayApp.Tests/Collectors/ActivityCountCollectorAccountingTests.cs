using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

namespace ONEVO.Agent.TrayApp.Tests.Collectors;

/// <summary>
/// UAT-01: snapshots report the time they actually cover. No hooks are installed (StartAsync is not
/// called), so these run headless.
/// </summary>
public class ActivityCountCollectorAccountingTests
{
    private long _nowMs = 1_000_000;
    private int _secondsSinceInput;

    private (ActivityCountCollector Collector, List<ActivitySnapshotPayload> Emitted) Create()
    {
        var collector = new ActivityCountCollector(
            NullLogger<ActivityCountCollector>.Instance,
            new NamedPipeClient(NullLogger<NamedPipeClient>.Instance),
            new FakeSessionDayMetrics(),
            () => _nowMs,
            () => _secondsSinceInput);
        var emitted = new List<ActivitySnapshotPayload>();
        collector.SnapshotEmitted += (payload, _) => emitted.Add(payload);
        return (collector, emitted);
    }

    [Fact]
    public async Task SnapshotAfterPartialInterval_CoversOnlyElapsedTime()
    {
        // Previously the first snapshot (5 s after start) and the final one on stop (break, lock,
        // disconnect) each reported a full 60 s interval.
        var (collector, emitted) = Create();

        _nowMs += 5_000;
        await collector.EmitSnapshotAsync(CancellationToken.None);
        _nowMs += 12_000;
        await collector.EmitSnapshotAsync(CancellationToken.None);

        Assert.Equal(new[] { 5, 12 }, emitted.Select(p => p.ActiveSeconds + p.IdleSeconds));
    }

    [Fact]
    public async Task IdleSpanEndingBeforeTheSnapshot_IsStillCountedAsIdle()
    {
        // 40 s without input, then 20 s of typing. Previously: idle 0 / active 60, because idle was
        // read only at the snapshot instant.
        var (collector, emitted) = Create();

        for (var t = 5; t <= 60; t += 5)
        {
            _nowMs += 5_000;
            _secondsSinceInput = t <= 40 ? t : 0;
            await collector.TickAsync(CancellationToken.None);
        }

        var snapshot = Assert.Single(emitted);
        Assert.Equal(40, snapshot.IdleSeconds);
        Assert.Equal(20, snapshot.ActiveSeconds);
    }

    [Fact]
    public async Task Tick_EmitsOnlyOnceTheIntervalHasElapsed()
    {
        var (collector, emitted) = Create();

        for (var i = 0; i < 11; i++)
        {
            _nowMs += 5_000;
            await collector.TickAsync(CancellationToken.None);
        }
        Assert.Empty(emitted); // 55 s

        _nowMs += 5_000;
        await collector.TickAsync(CancellationToken.None);
        Assert.Single(emitted); // 60 s
    }

    [Fact]
    public async Task NothingElapsed_EmitsNothing()
    {
        var (collector, emitted) = Create();

        await collector.EmitSnapshotAsync(CancellationToken.None);

        Assert.Empty(emitted);
    }
}
