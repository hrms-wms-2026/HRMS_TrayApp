using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class SetupFlowTests
{
    [Fact]
    public void FirstRunOrder_MatchesMockupSequence()
    {
        Assert.Equal("//review", SetupFlow.AfterActivation);
        Assert.Equal("//photo", SetupFlow.AfterConfirmDetails);
        Assert.Equal("//location?next=privacy", SetupFlow.AfterFaceEnrollment);
        Assert.Equal("//policy", SetupFlow.AfterPrivacy);
        Assert.Equal("//prepare", SetupFlow.AfterPermissions);
        Assert.Equal("//clockin", SetupFlow.AfterWorkspaceReady);
    }

    [Fact]
    public void DisplayOrDash_Empty_IsEmDash()
    {
        Assert.Equal("—", SetupFlow.DisplayOrDash(""));
        Assert.Equal("—", SetupFlow.DisplayOrDash("  "));
        Assert.Equal("Ada", SetupFlow.DisplayOrDash("Ada"));
    }
}
