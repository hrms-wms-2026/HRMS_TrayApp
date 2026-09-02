using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ReviewSetupViewModelTests
{
    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public void FaceVerificationCompleted_DefaultsFalse()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());
        Assert.False(vm.FaceVerificationCompleted);
    }

    [Fact]
    public void FaceVerificationStatusText_WhenNotCompleted()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore()) { FaceVerificationCompleted = false };
        Assert.NotEqual("Enrolled", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void FaceVerificationStatusText_WhenCompleted()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore()) { FaceVerificationCompleted = true };
        Assert.Equal("Enrolled", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void ConfirmAndContinueCommand_Exists()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());
        Assert.NotNull(vm.ConfirmAndContinueCommand);
    }

    [Fact]
    public void BackCommand_Exists()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());
        Assert.NotNull(vm.BackCommand);
    }

    [Fact]
    public void FullName_DefaultsEmpty()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());
        Assert.Equal(string.Empty, vm.FullName);
    }
}
