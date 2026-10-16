using Microsoft.AspNetCore.SignalR.Client;
using ONEVO.Agent.Service.Sync;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Sync;

public class PushChannelStateTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Slow = TimeSpan.FromHours(1);
    // Generous upper bound for "fast" assertions: with Slow = 1h the target is unreachable on the
    // slow cadence, so waiting until it is reached (instead of counting polls in a fixed window)
    // still proves the fast cadence without depending on CI runner speed or timer resolution.
    private static readonly TimeSpan FastDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Runs the loop until <paramref name="target"/> polls happen or the deadline passes.</summary>
    private static async Task<int> PollUntilAsync(PushChannelState state, int target, Func<bool>? forceFast = null)
    {
        var polls = 0;
        using var cts = new CancellationTokenSource(FastDeadline);
        try
        {
            await state.RunAdaptiveLoopAsync(Fast, Slow, _ =>
            {
                if (++polls >= target)
                    cts.Cancel();
                return Task.CompletedTask;
            }, cts.Token, forceFast);
        }
        catch (OperationCanceledException) { }
        return polls;
    }

    private static async Task<int> CountPollsAsync(PushChannelState state, TimeSpan runFor, Func<bool>? forceFast = null)
    {
        var polls = 0;
        using var cts = new CancellationTokenSource(runFor);
        try
        {
            await state.RunAdaptiveLoopAsync(Fast, Slow, _ => { polls++; return Task.CompletedTask; }, cts.Token, forceFast);
        }
        catch (OperationCanceledException) { }
        return polls;
    }

    [Fact]
    public async Task NoPush_PollsAtFastCadence()
    {
        var polls = await PollUntilAsync(new PushChannelState(), target: 4);
        Assert.True(polls >= 4, $"expected fast polling without push, got {polls}");
    }

    [Fact]
    public async Task PushActive_PollsOnceThenWaitsForSlowFallback()
    {
        var state = new PushChannelState();
        state.SetActive(true);

        var polls = await CountPollsAsync(state, TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, polls); // the initial poll only — the next is an hour away
    }

    [Fact]
    public async Task PushActive_ButForcedFast_KeepsFastCadence()
    {
        var state = new PushChannelState();
        state.SetActive(true);

        var polls = await PollUntilAsync(state, target: 4, forceFast: () => true);

        Assert.True(polls >= 4, $"expected fast polling while forced (e.g. on break), got {polls}");
    }

    [Fact]
    public async Task PushDropsMidWait_FallsBackToFastWithinOneTick()
    {
        var state = new PushChannelState();
        state.SetActive(true);
        var polls = 0;
        using var cts = new CancellationTokenSource(FastDeadline);

        var loop = state.RunAdaptiveLoopAsync(Fast, Slow, _ =>
        {
            if (++polls >= 3)
                cts.Cancel();
            return Task.CompletedTask;
        }, cts.Token);
        await Task.Delay(100);
        Assert.Equal(1, polls); // the first poll runs synchronously; the next is an hour away

        state.SetActive(false); // connection lost
        try { await loop; } catch (OperationCanceledException) { }

        Assert.True(polls >= 3, $"expected fast polling to resume after push dropped, got {polls}");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(500, 30)]
    public void ForeverRetryPolicy_NeverGivesUp(int previousRetries, int expectedSeconds)
    {
        var policy = new AgentCommandListener.ForeverRetryPolicy();

        var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetries });

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
