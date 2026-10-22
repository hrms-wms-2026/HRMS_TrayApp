using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrivacyConsentViewModelTests
{
    private static PrivacyConsentViewModel Make(FakePreferencesStore? prefs = null) =>
        new(new FakeNamedPipeClient());

    [Fact]
    public void ScreenMonitoringEnabled_DefaultsTrue()
    {
        var vm = Make();
        Assert.True(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void ScreenMonitoringEnabled_CannotBeTurnedOff()
    {
        var vm = Make();
        vm.ScreenMonitoringEnabled = false;
        Assert.True(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void AppTrackingEnabled_DefaultsTrue()
    {
        var vm = Make();
        Assert.True(vm.AppTrackingEnabled);
    }

    [Fact]
    public void LocationAccessEnabled_DefaultsTrue()
    {
        var vm = Make();
        Assert.True(vm.LocationAccessEnabled);
    }

    [Fact]
    public void CameraAccessEnabled_DefaultsFalse()
    {
        var vm = Make();
        Assert.False(vm.CameraAccessEnabled);
    }

    [Fact]
    public void KeyboardMouseEnabled_DefaultsTrue()
    {
        var vm = Make();
        Assert.True(vm.KeyboardMouseEnabled);
    }

    [Fact]
    public void AllowAndContinueCommand_AlwaysEnabled()
    {
        var vm = Make();
        Assert.True(vm.AllowAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task AllowAndContinue_DoesNotMarkSetupCompleteYet()
    {
        var prefs = new FakePreferencesStore();
        var vm = Make(prefs);

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        Assert.False(WorkLocationFlow.IsSetupComplete(prefs));
    }

    [Fact]
    public void ApplyPolicy_SetsAppTracking()
    {
        var vm     = Make();
        var policy = new AgentPolicy { Version = "1", AppUsageEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.AppTrackingEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsCameraAccess()
    {
        var vm     = Make();
        var policy = new AgentPolicy { Version = "1", CameraVerificationEnabled = true };
        vm.ApplyPolicy(policy);
        Assert.True(vm.CameraAccessEnabled);
    }

    [Fact]
    public void ApplyPolicy_TurnsCameraAccessOnWhenWorkModePhotoRequired()
    {
        var vm = Make();
        var policy = new AgentPolicy { Version = "1", PhotoRequiredEnabled = true, CameraVerificationEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.True(vm.CameraAccessEnabled);
    }

    [Fact]
    public async Task AllowAndContinue_SavesTheTogglesOnTheBackend()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new PrivacyConsentViewModel(pipe) { AppTrackingEnabled = false, CameraAccessEnabled = true };

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        var saved = Assert.Single(pipe.SubmittedPermissionConsents);
        Assert.True(saved.ScreenMonitoring);
        Assert.False(saved.AppTracking);
        Assert.True(saved.CameraAccess);
    }

    [Fact]
    public async Task AllowAndContinue_SaveFails_StillContinues()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextPermissionConsentSubmitResult = new(false, "SERVICE_UNAVAILABLE", false, null)
        };
        var vm = new PrivacyConsentViewModel(pipe);

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        Assert.Single(pipe.SubmittedPermissionConsents);
    }
}
