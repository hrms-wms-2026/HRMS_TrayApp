namespace ONEVO.Agent.TrayApp.Tests;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.TrayApp.Collectors;
using Xunit;

/// <summary>
/// ISS-014: active/idle seconds per snapshot. Interval 60 s, idle threshold 30 s (original rule:
/// idle when there has been no input for at least half the interval). Times are in seconds.
/// "Old" in comments = value produced by the previous snapshot-instant formula.
/// </summary>
public class ActivityIntervalTrackerTests
{
    /// <summary>Simulates sampling every 5 s; <paramref name="inputAt"/> lists seconds with input.</summary>
    private static (int Active, int Idle) Run(int seconds, params int[] inputAt) =>
        Run(seconds, sinceStartIdle: 0, inputAt);

    private static (int Active, int Idle) Run(int seconds, int sinceStartIdle, int[] inputAt)
    {
        const long origin = 1_000_000;
        var tracker = new ActivityIntervalTracker(60, 30, origin);
        long lastInput = origin - sinceStartIdle * 1000L;
        for (var t = 1; t <= seconds; t++)
        {
            var now = origin + t * 1000L;
            if (inputAt.Contains(t)) lastInput = now;
            if (t % 5 == 0 || t == seconds)
                tracker.Sample(now, (int)((now - lastInput) / 1000));
        }
        return tracker.Complete(origin + seconds * 1000L);
    }

    [Fact]
    public void ContinuousInput_IsFullyActive() =>
        Assert.Equal((60, 0), Run(60, Enumerable.Range(1, 60).ToArray()));

    [Fact]
    public void IdleThenActive_CountsTheIdleSpan()
    {
        // No input for 0-50 s, typing 51-60 s. Old: active 60, idle 0.
        Assert.Equal((10, 50), Run(60, Enumerable.Range(51, 10).ToArray()));
    }

    [Fact]
    public void ShortPauseBelowThreshold_IsActive()
    {
        // Typing until 31 s, then 29 s without input: below the 30 s threshold.
        Assert.Equal((60, 0), Run(60, Enumerable.Range(1, 31).ToArray()));
    }

    [Fact]
    public void TrailingIdleSpan_CountedFromLastInput()
    {
        // Typing until 20 s, then nothing: idle 40 s.
        Assert.Equal((20, 40), Run(60, Enumerable.Range(1, 20).ToArray()));
    }

    [Fact]
    public void InputEndsIdleSpan_ShortRemainderStaysActive()
    {
        // Idle 0-35 s (35 s), input 36-38 s, then 22 s without input (below threshold).
        var result = Run(60, 36, 37, 38);
        Assert.Equal(35, result.Idle);
        Assert.Equal(60, result.Active + result.Idle);
    }

    [Fact]
    public void PartialInterval_ReportsOnlyElapsedTime()
    {
        // Stop/pause 10 s after the last snapshot. Old: 60 s reported (double counting).
        var result = Run(10, Enumerable.Range(1, 10).ToArray());
        Assert.Equal((10, 0), result);
    }

    [Fact]
    public void IdleContinuingAcrossBoundary_CountsInNextIntervalFromItsStart()
    {
        const long origin = 0;
        var tracker = new ActivityIntervalTracker(60, 30, origin);
        tracker.Sample(60_000, 45); // last input at 15 s
        Assert.Equal((15, 45), tracker.Complete(60_000));

        tracker.Sample(120_000, 105); // still no input
        Assert.Equal((0, 60), tracker.Complete(120_000));
    }

    [Fact]
    public void ResumeFromSleep_NeverReportsMoreThanOneInterval()
    {
        var tracker = new ActivityIntervalTracker(60, 30, 0);
        tracker.Sample(5_000, 0);
        // Machine slept for two hours; first sample after resume.
        tracker.Sample(7_200_000, 7_195);
        var (active, idle) = tracker.Complete(7_200_000);
        Assert.Equal(60, active + idle);
        Assert.True(idle >= 55);
    }
}

public class ActivityCountCollectorTests
{
    private long _now = 1_000_000;

    private ActivityCountCollector Create(Func<IReadOnlyList<CollectionRecord>, CancellationToken, Task> submit) =>
        new(NullLogger<ActivityCountCollector>.Instance, submit, () => _now, () => 0 /* user continuously active */);

    [Fact]
    public async Task SnapshotAfterPartialInterval_CoversOnlyElapsedTime()
    {
        // ISS-014 regression: the final snapshot on stop/pause always claimed a full interval.
        var sent = new List<CollectionRecord>();
        var collector = Create((r, _) => { sent.AddRange(r); return Task.CompletedTask; });

        _now += 12_000;
        await collector.EmitSnapshotAsync(CancellationToken.None);

        var payload = Assert.Single(sent).Payload.Deserialize<ActivitySnapshotPayload>()!;
        Assert.Equal(12, payload.ActiveSeconds + payload.IdleSeconds);
    }

    [Fact]
    public async Task Tick_EmitsOnlyWhenIntervalElapsed()
    {
        var sent = 0;
        var collector = Create((_, _) => { sent++; return Task.CompletedTask; });

        for (var i = 0; i < 11; i++) { _now += 5_000; await collector.TickAsync(CancellationToken.None); }
        Assert.Equal(0, sent); // 55 s

        _now += 5_000;
        await collector.TickAsync(CancellationToken.None);
        Assert.Equal(1, sent); // 60 s
    }

    [Fact]
    public async Task Tick_SurvivesSubmitFailure_AndKeepsEmitting()
    {
        // ISS-017 regression: an IOException from the pipe used to end the snapshot loop for good.
        var attempts = 0;
        var collector = Create((_, _) =>
        {
            attempts++;
            return attempts == 1 ? Task.FromException(new IOException("pipe broken")) : Task.CompletedTask;
        });

        _now += 60_000;
        await collector.TickAsync(CancellationToken.None); // must not throw
        _now += 60_000;
        await collector.TickAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
    }
}
