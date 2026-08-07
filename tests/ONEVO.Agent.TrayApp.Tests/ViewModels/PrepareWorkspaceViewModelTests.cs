using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrepareWorkspaceViewModelTests
{
    [Fact]
    public void InitialState_AllStepsFalse()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.False(vm.ActivationVerified);
        Assert.False(vm.UserDetailsFetched);
        Assert.False(vm.WorkspacePrepared);
    }

    [Fact]
    public void CanContinue_FalseUntilAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_TrueWhenAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel();
        vm.ActivationVerified = true;
        vm.UserDetailsFetched = true;
        vm.WorkspacePrepared  = true;
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_SetsAllStepsAndUserFields()
    {
        var vm = new PrepareWorkspaceViewModel();
        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.ActivationVerified);
        Assert.True(vm.UserDetailsFetched);
        Assert.True(vm.WorkspacePrepared);
        Assert.True(vm.CanContinue);
        Assert.False(vm.IsLoading);
        Assert.NotEmpty(vm.EmployeeFullName);
        Assert.NotEmpty(vm.EmployeeEmail);
        Assert.NotEmpty(vm.EmployeeId);
    }
}
