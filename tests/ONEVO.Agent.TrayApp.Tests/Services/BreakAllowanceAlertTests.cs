namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

[CollectionDefinition(nameof(BreakAllowanceAlertTests), DisableParallelization = true)]
public sealed class BreakAllowanceAlertCollection;

// The gate is process-wide static state, so these must not run alongside other tests.
[Collection(nameof(BreakAllowanceAlertTests))]
public sealed class BreakAllowanceAlertTests : IDisposable
{
    private DateOnly _today = new(2026, 10, 23);

    public BreakAllowanceAlertTests()
    {
        BreakAllowanceAlert.ResetForTests();
        BreakAllowanceAlert.Today = () => _today;
    }

    public void Dispose() => BreakAllowanceAlert.ResetForTests();

    [Fact]
    public void TryClaim_FirstCallerWins_SecondStaysSilent()
    {
        // Live session timer and polled server notification both try to show the same alert.
        Assert.True(BreakAllowanceAlert.TryClaim());
        Assert.False(BreakAllowanceAlert.TryClaim());
    }

    [Fact]
    public void TryClaim_NextLocalDay_AlertsAgain()
    {
        Assert.True(BreakAllowanceAlert.TryClaim());

        _today = _today.AddDays(1);

        Assert.True(BreakAllowanceAlert.TryClaim());
        Assert.False(BreakAllowanceAlert.TryClaim());
    }

    [Fact]
    public async Task TryClaim_ConcurrentCallers_ExactlyOneWins()
    {
        var results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(BreakAllowanceAlert.TryClaim)));

        Assert.Equal(1, results.Count(claimed => claimed));
    }
}
