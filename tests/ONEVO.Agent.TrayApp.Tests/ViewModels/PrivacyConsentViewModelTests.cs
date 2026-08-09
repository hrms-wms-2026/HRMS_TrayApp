using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrivacyConsentViewModelTests
{
    [Fact]
    public void ScreenMonitoringEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void AppTrackingEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.AppTrackingEnabled);
    }

    [Fact]
    public void CameraAccessEnabled_DefaultsFalse()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.False(vm.CameraAccessEnabled);
    }

    [Fact]
    public void KeyboardMouseEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.KeyboardMouseEnabled);
    }

    [Fact]
    public void AllowAndContinueCommand_AlwaysEnabled()
    {
        var vm = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        Assert.True(vm.AllowAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyPolicy_SetsAppTracking()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", AppUsageEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.AppTrackingEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsCameraAccess()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", CameraVerificationEnabled = true };
        vm.ApplyPolicy(policy);
        Assert.True(vm.CameraAccessEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsScreenMonitoring()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", ScreenshotEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsKeyboardMouse()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", ActivitySignalEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.KeyboardMouseEnabled);
    }
}
