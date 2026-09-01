using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class WorkLocationFlowTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Yesterday = Today.AddDays(-1);

    [Fact]
    public void RouteWhenStopped_DuringSetup_DoesNotNavigate()
    {
        var prefs = new FakePreferencesStore();
        Assert.Equal(string.Empty, WorkLocationFlow.RouteWhenStopped(prefs, Today));
    }

    [Fact]
    public void RouteWhenStopped_SetupCompleteWithoutTodayLocation_GoesToLocationThenClockIn()
    {
        var prefs = new FakePreferencesStore();
        WorkLocationFlow.MarkSetupComplete(prefs);

        Assert.Equal(WorkLocationFlow.LocationThenClockIn, WorkLocationFlow.RouteWhenStopped(prefs, Today));
    }

    [Fact]
    public void RouteWhenStopped_SetupCompleteAndConfirmedToday_GoesToClockIn()
    {
        var prefs = new FakePreferencesStore();
        WorkLocationFlow.MarkSetupComplete(prefs);
        WorkLocationFlow.MarkConfirmedToday(prefs, Today);

        Assert.Equal(WorkLocationFlow.ClockInRoute, WorkLocationFlow.RouteWhenStopped(prefs, Today));
    }

    [Fact]
    public void RouteToStartWork_YesterdayConfirmation_RequiresLocationAgain()
    {
        var prefs = new FakePreferencesStore();
        WorkLocationFlow.MarkConfirmedToday(prefs, Yesterday);

        Assert.Equal(WorkLocationFlow.LocationThenClockIn, WorkLocationFlow.RouteToStartWork(prefs, Today));
    }

    [Fact]
    public void ResolveNextRoute_ClockInQuery_ReturnsClockIn()
    {
        Assert.Equal(WorkLocationFlow.ClockInRoute, WorkLocationFlow.ResolveNextRoute("clockin"));
    }

    [Fact]
    public void ResolveNextRoute_MissingOrPrepare_ReturnsPrepare()
    {
        Assert.Equal(WorkLocationFlow.PrepareRoute, WorkLocationFlow.ResolveNextRoute(null));
        Assert.Equal(WorkLocationFlow.PrepareRoute, WorkLocationFlow.ResolveNextRoute("prepare"));
        Assert.Equal(WorkLocationFlow.PrepareRoute, WorkLocationFlow.ResolveNextRoute("unknown"));
    }

    [Fact]
    public void SignOut_ClearsLocationDayAndSetupFlags()
    {
        var prefs = new FakePreferencesStore();
        WorkLocationFlow.MarkSetupComplete(prefs);
        WorkLocationFlow.MarkConfirmedToday(prefs, Today);

        SessionPreferenceKeys.ClearAll(prefs);

        Assert.False(WorkLocationFlow.IsSetupComplete(prefs));
        Assert.False(WorkLocationFlow.IsConfirmedToday(prefs, Today));
    }
}
