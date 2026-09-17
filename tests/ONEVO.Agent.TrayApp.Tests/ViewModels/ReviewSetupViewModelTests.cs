using ONEVO.Agent.TrayApp.Services;
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

    [Fact]
    public void OnAppearing_LoadsDepartmentOfficeAndWorkModeFromSession()
    {
        var prefs = new FakePreferencesStore();
        prefs.Set(SessionPreferenceKeys.EmployeeDisplayName, "Dapi Owner");
        prefs.Set(SessionPreferenceKeys.EmployeeEmail, "dapiyshanth1908@gmail.com");
        prefs.Set(SessionPreferenceKeys.EmployeeId, "DAPI-0001");
        prefs.Set(SessionPreferenceKeys.Department, "Executive & Leadership");
        prefs.Set(SessionPreferenceKeys.OfficeName, "Dapi Technologies");
        prefs.Set(SessionPreferenceKeys.WorkMode, "Onsite");
        prefs.Set(SessionPreferenceKeys.DeviceName, "TICS16");

        var vm = new ReviewSetupViewModel(prefs);
        vm.OnAppearing();

        Assert.Equal("Dapi Owner", vm.FullName);
        Assert.Equal("dapiyshanth1908@gmail.com", vm.WorkEmail);
        Assert.Equal("DAPI-0001", vm.EmployeeId);
        Assert.Equal("Executive & Leadership", vm.Department);
        Assert.Equal("Dapi Technologies", vm.RegisteredOffice);
        Assert.Equal("Onsite", vm.WorkMode);
        Assert.Equal("TICS16", vm.ThisDevice);
    }
}
