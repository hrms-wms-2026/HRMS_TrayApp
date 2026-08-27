using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ReviewSetupViewModelTests
{
    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new ReviewSetupViewModel();
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public void FaceVerificationCompleted_DefaultsFalse()
    {
        var vm = new ReviewSetupViewModel();
        Assert.False(vm.FaceVerificationCompleted);
    }

    [Fact]
    public void FaceVerificationStatusText_WhenNotCompleted()
    {
        var vm = new ReviewSetupViewModel { FaceVerificationCompleted = false };
        Assert.NotEqual("Completed", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void FaceVerificationStatusText_WhenCompleted()
    {
        var vm = new ReviewSetupViewModel { FaceVerificationCompleted = true };
        Assert.Equal("Completed", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void ConfirmAndContinueCommand_Exists()
    {
        var vm = new ReviewSetupViewModel();
        Assert.NotNull(vm.ConfirmAndContinueCommand);
    }

    [Fact]
    public void BackCommand_Exists()
    {
        var vm = new ReviewSetupViewModel();
        Assert.NotNull(vm.BackCommand);
    }

    [Fact]
    public void FullName_DefaultsEmpty()
    {
        var vm = new ReviewSetupViewModel();
        Assert.Equal(string.Empty, vm.FullName);
    }
}
