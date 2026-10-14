using Microsoft.AspNetCore.SignalR.Client;
using ONEVO.Agent.Service.Sync;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Sync;

public class PushChannelStateTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Slow = TimeSpan.FromHours(1);

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
        var polls = await CountPollsAsync(new PushChannelState(), TimeSpan.FromMilliseconds(300));
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

        var polls = await CountPollsAsync(state, TimeSpan.FromMilliseconds(300), forceFast: () => true);

        Assert.True(polls >= 4, $"expected fast polling while forced (e.g. on break), got {polls}");
    }

    [Fact]
    public async Task PushDropsMidWait_FallsBackToFastWithinOneTick()
    {
        var state = new PushChannelState();
        state.SetActive(true);
        var polls = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var loop = state.RunAdaptiveLoopAsync(Fast, Slow, _ => { polls++; return Task.CompletedTask; }, cts.Token);
        await Task.Delay(100);
        Assert.Equal(1, polls);

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
