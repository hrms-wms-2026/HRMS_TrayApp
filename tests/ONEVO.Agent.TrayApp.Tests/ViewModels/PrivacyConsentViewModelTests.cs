using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrivacyConsentViewModelTests
{
    private static PrivacyConsentViewModel Make(FakePreferencesStore? prefs = null) =>
        new(new FakeNamedPipeClient(), prefs ?? new FakePreferencesStore());

    [Fact]
    public void ScreenMonitoringEnabled_DefaultsTrue()
    {
        var vm = Make();
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
    public async Task AllowAndContinue_MarksSetupComplete()
    {
        var prefs = new FakePreferencesStore();
        var vm = Make(prefs);

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        Assert.True(WorkLocationFlow.IsSetupComplete(prefs));
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
}
