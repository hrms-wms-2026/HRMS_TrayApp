using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
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

    private static readonly PermissionConsentPayload AllAllowed = new(true, true, true, true, true, true);

    private static PermissionConsentResultPayload Accepted(PermissionConsentPayload consent) =>
        new(true, null, true, consent);

    private static FaceReferenceStatusResultPayload FaceOnFile => new(true, null, true, 1);

    [Fact]
    public async Task ConfirmAndContinue_NoPipe_GoesToFaceSetup()
    {
        var vm = new ReviewSetupViewModel(new FakePreferencesStore());

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.FaceEnrollment, vm.LastRoute);
    }

    [Fact]
    public async Task ConfirmAndContinue_ServiceDoesNotAnswer_GoesToFaceSetup()
    {
        var prefs = new FakePreferencesStore();
        var vm = new ReviewSetupViewModel(prefs, pipe: new FakeNamedPipeClient());

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.FaceEnrollment, vm.LastRoute);
        Assert.Equal("false", prefs.Get(SessionPreferenceKeys.PermissionConsentOnFile, string.Empty));
    }

    [Fact]
    public async Task ConfirmAndContinue_FaceAndConsentOnFile_SkipsToPrepare()
    {
        var prefs = new FakePreferencesStore();
        var pipe = new FakeNamedPipeClient
        {
            NextFaceReferenceStatus = FaceOnFile,
            NextPermissionConsentStatus = Accepted(AllAllowed)
        };
        var vm = new ReviewSetupViewModel(prefs, pipe: pipe);

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.Prepare, vm.LastRoute);
        Assert.True(vm.FaceVerificationCompleted);
        Assert.Equal("true", prefs.Get(SessionPreferenceKeys.FaceVerified, string.Empty));
    }

    [Fact]
    public async Task ConfirmAndContinue_FaceAndConsentOnFile_DailyLocationChoice_GoesToLocationThenPrepare()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFaceReferenceStatus = FaceOnFile,
            NextPermissionConsentStatus = Accepted(AllAllowed),
            LastKnownPolicy = new AgentPolicy { Version = "1", AllowsDailyLocationChoice = true }
        };
        var vm = new ReviewSetupViewModel(new FakePreferencesStore(), pipe: pipe);

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(WorkLocationFlow.LocationThenPrepare, vm.LastRoute);
    }

    [Fact]
    public async Task ConfirmAndContinue_FaceOnFile_NoConsent_GoesToPrivacy()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFaceReferenceStatus = FaceOnFile,
            NextPermissionConsentStatus = new PermissionConsentResultPayload(true, null, false, null)
        };
        var vm = new ReviewSetupViewModel(new FakePreferencesStore(), pipe: pipe);

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.Privacy, vm.LastRoute);
    }

    [Fact]
    public async Task ConfirmAndContinue_ConsentOnFile_NoFace_GoesToFaceSetupAndRemembersConsent()
    {
        var prefs = new FakePreferencesStore();
        var pipe = new FakeNamedPipeClient
        {
            NextFaceReferenceStatus = new FaceReferenceStatusResultPayload(true, null, false, 0),
            NextPermissionConsentStatus = Accepted(AllAllowed)
        };
        var vm = new ReviewSetupViewModel(prefs, pipe: pipe);

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.FaceEnrollment, vm.LastRoute);
        Assert.Equal("true", prefs.Get(SessionPreferenceKeys.PermissionConsentOnFile, string.Empty));
    }

    [Fact]
    public async Task ConfirmAndContinue_PolicyNowNeedsCameraNotAllowedBefore_ShowsScreensAgain()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFaceReferenceStatus = FaceOnFile,
            NextPermissionConsentStatus = Accepted(AllAllowed with { CameraAccess = false }),
            LastKnownPolicy = new AgentPolicy { Version = "1", CameraVerificationEnabled = true }
        };
        var vm = new ReviewSetupViewModel(new FakePreferencesStore(), pipe: pipe);

        await vm.ConfirmAndContinueCommand.ExecuteAsync(null);

        Assert.Equal(SetupFlow.Privacy, vm.LastRoute);
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
