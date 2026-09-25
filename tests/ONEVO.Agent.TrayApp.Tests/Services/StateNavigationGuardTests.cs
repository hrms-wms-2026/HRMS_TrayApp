using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class StateNavigationGuardTests
{
    [Theory]
    [InlineData("//photo")]
    [InlineData("//photo/identity-verification")]
    public void SameStateRebroadcast_OnFaceCapture_HoldsPage(string location)
    {
        Assert.True(StateNavigationGuard.ShouldHoldCurrentPage(
            location, MonitoringState.Stopped, MonitoringState.Stopped));
    }

    [Fact]
    public void ClockOutCapture_ActiveRebroadcast_HoldsPage()
    {
        Assert.True(StateNavigationGuard.ShouldHoldCurrentPage(
            "//photo", MonitoringState.Active, MonitoringState.Active));
    }

    [Theory]
    [InlineData(MonitoringState.Active)]
    [InlineData(MonitoringState.Locked)]
    [InlineData(MonitoringState.Unenrolled)]
    public void RealStateChange_OnFaceCapture_Navigates(MonitoringState next)
    {
        Assert.False(StateNavigationGuard.ShouldHoldCurrentPage(
            "//photo", MonitoringState.Stopped, next));
    }

    [Fact]
    public void FirstStateAfterLaunch_Navigates()
    {
        Assert.False(StateNavigationGuard.ShouldHoldCurrentPage(
            "//photo", null, MonitoringState.Stopped));
    }

    [Theory]
    [InlineData("//clockin")]
    [InlineData("//active")]
    [InlineData(null)]
    public void OtherPages_KeepExistingRouting(string? location)
    {
        Assert.False(StateNavigationGuard.ShouldHoldCurrentPage(
            location, MonitoringState.Stopped, MonitoringState.Stopped));
    }
}
