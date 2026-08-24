namespace ONEVO.Agent.TrayApp.Tests.Notifications;

using ONEVO.Agent.TrayApp.Services;

public sealed class NotificationActivationRouterTests
{
    private static readonly Guid AttemptId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("attempt=11111111-1111-1111-1111-111111111111;decision=allow", InactivityPromptDecision.Allowed)]
    [InlineData("attempt=11111111-1111-1111-1111-111111111111;decision=skip", InactivityPromptDecision.Declined)]
    public async Task Routes_only_known_attempt_and_decision(string args, InactivityPromptDecision expected)
    {
        var router = new NotificationActivationRouter();
        var pending = router.WaitAsync(AttemptId, default);
        router.Route(args);
        Assert.Equal(expected, await pending);
    }

    [Fact]
    public void Route_UnknownAttemptId_IsSafeNoOp()
    {
        var router = new NotificationActivationRouter();

        // Nobody is waiting on this attempt id — must not throw.
        var exception = Record.Exception(() =>
            router.Route("attempt=22222222-2222-2222-2222-222222222222;decision=allow"));

        Assert.Null(exception);
    }

    [Fact]
    public void Route_MalformedArguments_IsSafeNoOp()
    {
        var router = new NotificationActivationRouter();
        var pending = router.WaitAsync(AttemptId, default);

        var exception = Record.Exception(() => router.Route("not-a-query-string"));

        Assert.Null(exception);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public void Route_UnknownDecisionValue_IsSafeNoOp()
    {
        var router = new NotificationActivationRouter();
        var pending = router.WaitAsync(AttemptId, default);

        router.Route($"attempt={AttemptId};decision=maybe");

        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task Route_DuplicateActivation_DoesNotThrowOrDoubleComplete()
    {
        var router = new NotificationActivationRouter();
        var pending = router.WaitAsync(AttemptId, default);
        var args = $"attempt={AttemptId};decision=allow";

        router.Route(args);
        var exception = Record.Exception(() => router.Route(args));

        Assert.Null(exception);
        Assert.Equal(InactivityPromptDecision.Allowed, await pending);
    }

    [Fact]
    public async Task WaitAsync_TokenCancelledBeforeRoute_ObservesCancellation_DoesNotHang()
    {
        var router = new NotificationActivationRouter();
        using var cts = new CancellationTokenSource();
        var pending = router.WaitAsync(AttemptId, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);
    }

    [Fact]
    public async Task WaitAsync_TokenAlreadyCancelled_ObservesCancellationImmediately()
    {
        var router = new NotificationActivationRouter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pending = router.WaitAsync(AttemptId, cts.Token);

        await Assert.ThrowsAsync<TaskCanceledException>(() => pending);
    }

    // NOTE on expiry: the router deliberately owns no timer. `IInactivityPromptService.PromptAsync`
    // receives `expiresIn` as an explicit parameter (not `WaitAsync`), so expiry is the prompt
    // service's responsibility — it links the caller's CancellationToken with a
    // `CancelAfter(expiresIn)` timer and passes that single linked token into `WaitAsync`. The
    // router only ever needs to answer "cancelled or not", which keeps it free of Windows App SDK
    // and timer dependencies and fully unit-testable via the cancellation tests above.
}
