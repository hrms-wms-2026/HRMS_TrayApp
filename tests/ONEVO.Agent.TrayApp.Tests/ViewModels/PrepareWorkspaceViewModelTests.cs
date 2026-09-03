using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrepareWorkspaceViewModelTests
{
    private static PrepareWorkspaceViewModel MakeVm(
        FakePreferencesStore? preferences = null,
        FakeWorkLocationStore? workLocationStore = null) =>
        new(preferences ?? new FakePreferencesStore(), workLocationStore ?? new FakeWorkLocationStore());

    private static WorkLocationReference AReference() => new(
        WorkLocationKind.Office, "OFFICE", "Office",
        6.9271, 79.8612, 12, 300, DateTimeOffset.UtcNow);

    [Fact]
    public void InitialState_AllStepsFalse()
    {
        var vm = MakeVm();
        Assert.False(vm.ActivationVerified);
        Assert.False(vm.UserDetailsFetched);
        Assert.False(vm.WorkspacePrepared);
    }

    [Fact]
    public void CanContinue_FalseUntilAllStepsComplete()
    {
        var vm = MakeVm();
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_FalseUntilLocationConfirmed()
    {
        var vm = MakeVm();
        vm.ActivationVerified = true;
        vm.UserDetailsFetched = true;
        vm.WorkspacePrepared  = true;
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_TrueWhenAllStepsComplete()
    {
        var vm = MakeVm(workLocationStore: new FakeWorkLocationStore { Value = AReference() });
        vm.ActivationVerified = true;
        vm.UserDetailsFetched = true;
        vm.WorkspacePrepared  = true;
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = MakeVm();
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_SetsAllStepsAndUserFields()
    {
        var preferences = new FakePreferencesStore();
        preferences.Set("onevo.employee_display_name", "Existing Name");
        preferences.Set("onevo.employee_email", "existing@test.dev");
        preferences.Set("onevo.employee_id", "EMP-EXISTING");
        var vm = MakeVm(preferences, new FakeWorkLocationStore { Value = AReference() });

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
        var vm = MakeVm(preferences);

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("Cached Name", vm.EmployeeFullName);
        Assert.Equal("cached@test.dev", vm.EmployeeEmail);
        Assert.Equal("EMP-CACHED", vm.EmployeeId);
        Assert.NotEqual("Pirakeerthan", vm.EmployeeFullName);
    }

    [Fact]
    public async Task LoadAsync_NoCachedPreferences_LeavesFieldsEmpty()
    {
        var vm = MakeVm();

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, vm.EmployeeFullName);
        Assert.Equal(string.Empty, vm.EmployeeEmail);
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_WithoutLocation_StillFinishesSettingUp()
    {
        var prefs = new FakePreferencesStore();
        var vm = MakeVm(prefs);
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.CanContinue);
        Assert.True(vm.ShouldOpenLocation);
        Assert.Equal("setting", vm.Stage);
        Assert.True(WorkLocationFlow.IsSetupComplete(prefs));
        Assert.True(vm.WorkspacePrepared);
    }

    [Fact]
    public async Task LoadAsync_WithLocationAlreadySaved_DoesNotNeedLocationScreen()
    {
        var vm = MakeVm(workLocationStore: new FakeWorkLocationStore { Value = AReference() });
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.ShouldOpenLocation);
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public async Task LoadAsync_FinishesSettingUpAndMarksComplete()
    {
        var prefs = new FakePreferencesStore();
        var vm = MakeVm(prefs, new FakeWorkLocationStore { Value = AReference() });
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("setting", vm.Stage);
        Assert.True(vm.ShowSettingUp);
        Assert.False(vm.ShowWelcomeBack);
        Assert.Equal(100, vm.ProgressPercent);
        Assert.Equal(100, vm.SettingProgressPercent);
        Assert.True(vm.ActivationVerified);
        Assert.True(vm.DeviceRegistered);
        Assert.True(vm.WorkspacePrepared);
        Assert.False(vm.IsLoading);
        Assert.True(WorkLocationFlow.IsSetupComplete(prefs));
    }

    [Fact]
    public void SettingProgressPercent_TracksCompletedSteps()
    {
        var vm = MakeVm();
        Assert.Equal(12, vm.SettingProgressPercent);

        vm.ActivationVerified = true;
        Assert.Equal(25, vm.SettingProgressPercent);
        Assert.Equal("Completed", vm.ActivationStepStatus);
        Assert.True(vm.DetailsInProgress);

        vm.UserDetailsFetched = true;
        vm.DeviceRegistered = true;
        vm.WorkspacePrepared = true;
        Assert.Equal(100, vm.SettingProgressPercent);
        Assert.True(vm.ShowSettingCheck);
        Assert.Equal("Completed", vm.WorkspaceStepStatus);
    }

    [Fact]
    public void ActivationStepStatus_InProgressUntilVerified()
    {
        var vm = MakeVm();
        Assert.Equal("In progress", vm.ActivationStepStatus);

        vm.ActivationVerified = true;
        Assert.Equal("Completed", vm.ActivationStepStatus);
        Assert.False(vm.ActivationInProgress);
    }

    [Fact]
    public async Task LoadAsync_WhenSetupAlreadyComplete_ShowsWelcomeBack()
    {
        var prefs = new FakePreferencesStore();
        WorkLocationFlow.MarkSetupComplete(prefs);
        prefs.Set("onevo.employee_display_name", "Acme Owner");
        var vm = MakeVm(prefs, new FakeWorkLocationStore { Value = AReference() });

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("welcome", vm.Stage);
        Assert.True(vm.ShowWelcomeBack);
        Assert.False(vm.ShowSettingUp);
        Assert.True(vm.WelcomeWorkspaceDone);
        Assert.Equal("Acme Owner", vm.EmployeeFullName);
        Assert.Equal(100, vm.ProgressPercent);
    }

    [Fact]
    public void IsLocationConfirmed_FalseWhenNoReferenceSaved()
    {
        var vm = MakeVm();
        Assert.False(vm.IsLocationConfirmed);
    }

    [Fact]
    public void IsLocationConfirmed_TrueWhenReferenceAlreadySaved()
    {
        var store = new FakeWorkLocationStore { Value = AReference() };
        var vm = MakeVm(workLocationStore: store);
        Assert.True(vm.IsLocationConfirmed);
    }

    [Fact]
    public void RefreshLocationStatus_PicksUpReferenceSavedAfterConstruction()
    {
        var store = new FakeWorkLocationStore();
        var vm = MakeVm(workLocationStore: store);
        Assert.False(vm.IsLocationConfirmed);

        store.Value = AReference();
        vm.RefreshLocationStatus();

        Assert.True(vm.IsLocationConfirmed);
        Assert.Contains("Office", vm.LocationStatusText);
    }
}
