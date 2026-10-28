namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class SingleInstanceGuardTests
{
    private static string UniqueName() => $@"Local\ONEVO.Agent.TrayApp.Tests.{Guid.NewGuid():N}";

    // A Mutex is re-entrant for its owning thread, so a "second launch" must run on another thread.
    private static SingleInstanceGuard? AcquireOnOtherThread(string name)
    {
        SingleInstanceGuard? result = null;
        var thread = new Thread(() => result = SingleInstanceGuard.TryAcquire(name));
        thread.Start();
        thread.Join();
        return result;
    }

    [Fact]
    public void TryAcquire_FirstInstance_GetsGuard_SecondDoesNot()
    {
        var name = UniqueName();
        using var first = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(AcquireOnOtherThread(name));
    }

    [Fact]
    public async Task TryAcquire_SecondLaunch_SignalsFirstToShowWindow()
    {
        var name = UniqueName();
        using var first = SingleInstanceGuard.TryAcquire(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first!.OnActivationRequested(() => activated.TrySetResult());

        AcquireOnOtherThread(name);

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void TryAcquire_AfterFirstDisposed_NextLaunchBecomesPrimary()
    {
        var name = UniqueName();
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using var next = AcquireOnOtherThread(name);

        Assert.NotNull(next);
    }
}
