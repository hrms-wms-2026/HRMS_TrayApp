using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrepareWorkspaceViewModelTests
{
    [Fact]
    public void InitialState_AllStepsFalse()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.False(vm.ActivationVerified);
        Assert.False(vm.UserDetailsFetched);
        Assert.False(vm.WorkspacePrepared);
    }

    [Fact]
    public void CanContinue_FalseUntilAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_TrueWhenAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        vm.ActivationVerified = true;
        vm.UserDetailsFetched = true;
        vm.WorkspacePrepared  = true;
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_SetsAllStepsAndUserFields()
    {
        var preferences = new FakePreferencesStore();
        preferences.Set("onevo.employee_display_name", "Existing Name");
        preferences.Set("onevo.employee_email", "existing@test.dev");
        preferences.Set("onevo.employee_id", "EMP-EXISTING");
        var vm = new PrepareWorkspaceViewModel(preferences);

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

    [Fact]
    public async Task LoadAsync_UsesCachedPreferences_NotHardcodedValues()
    {
        var preferences = new FakePreferencesStore();
        preferences.Set("onevo.employee_display_name", "Cached Name");
        preferences.Set("onevo.employee_email", "cached@test.dev");
        preferences.Set("onevo.employee_id", "EMP-CACHED");
        var vm = new PrepareWorkspaceViewModel(preferences);

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("Cached Name", vm.EmployeeFullName);
        Assert.Equal("cached@test.dev", vm.EmployeeEmail);
        Assert.Equal("EMP-CACHED", vm.EmployeeId);
        Assert.NotEqual("Pirakeerthan", vm.EmployeeFullName);
    }

    [Fact]
    public async Task LoadAsync_NoCachedPreferences_LeavesFieldsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, vm.EmployeeFullName);
        Assert.Equal(string.Empty, vm.EmployeeEmail);
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_NotifiesContinueSetupCommandCanExecuteChanged()
    {
        var vm = new PrepareWorkspaceViewModel(new FakePreferencesStore());
        var fired = false;
        vm.ContinueSetupCommand.CanExecuteChanged += (_, _) => fired = true;

        await vm.LoadAsync(CancellationToken.None);

        Assert.True(fired, "Continue button must be told to re-check CanExecute once setup finishes");
        Assert.True(vm.ContinueSetupCommand.CanExecute(null));
    }
}
