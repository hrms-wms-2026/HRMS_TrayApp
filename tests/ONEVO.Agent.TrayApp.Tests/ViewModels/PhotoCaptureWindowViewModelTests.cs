using System.Text.Json;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PhotoCaptureWindowViewModelTests
{
    private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true) =>
        new(new FakeCameraService { ShouldReturnPhoto = cameraSucceeds },
            new FakeNamedPipeClient(),
            new FakePreferencesStore(),
            new CapturedPhotoBuffer());

    [Fact]
    public void InitialState_NotCaptured()
    {
        var vm = MakeVm();
        Assert.False(vm.IsCaptured);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public void ContinueCommand_DisabledBeforeCapture()
    {
        var vm = MakeVm();
        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task CapturePhotoCommand_SetsIsCapturedOnSuccess()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.True(vm.IsCaptured);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public async Task CapturePhotoCommand_ExposesCapturedBytesForConfirmation()
    {
        var vm = MakeVm(cameraSucceeds: true);

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.Equal([0xFF, 0xD8, 0xFF], vm.CapturedPhotoBytes);
    }

    [Fact]
    public async Task SetContext_ClearsCapturedPhotoConfirmation()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);

        vm.SetContext("clockin");

        Assert.Null(vm.CapturedPhotoBytes);
    }

    [Fact]
    public void SetContext_ClockOut_UsesVerifyYourIdentityTitle()
    {
        var vm = MakeVm();
        vm.SetContext("clockout");
        Assert.Equal("Verify Your Identity", vm.Headline);
    }

    [Fact]
    public async Task CapturePhotoCommand_IsCapturedFalseWhenCameraReturnsNull()
    {
        var vm = MakeVm(cameraSucceeds: false);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.False(vm.IsCaptured);
    }

    [Fact]
    public async Task ContinueCommand_EnabledAfterSuccessfulCapture()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void CaptureStatusText_DefaultsToPrompt()
    {
        var vm = MakeVm();
        Assert.False(string.IsNullOrWhiteSpace(vm.CaptureStatusText));
    }

    [Fact]
    public async Task CaptureStatusText_UpdatesAfterSuccessfulCapture()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.Contains("captured", vm.CaptureStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CapturePhotoCommand_CallsCameraService()
    {
        var fake = new FakeCameraService { ShouldReturnPhoto = true };
        var vm   = new PhotoCaptureWindowViewModel(fake, new FakeNamedPipeClient(), new FakePreferencesStore(), new CapturedPhotoBuffer());
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Continue_EmbedsCapturedGpsFromPrefsIntoFacePhotoRecord()
    {
        var prefs = new FakePreferencesStore();
        prefs.Set("onevo.live_latitude",         (13.0827).ToString("G17"));
        prefs.Set("onevo.live_longitude",        (80.2707).ToString("G17"));
        prefs.Set("onevo.work_location_display", "Chennai Office");

        var pipe = new FakeNamedPipeClient();
        var vm   = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, prefs, new CapturedPhotoBuffer());

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        var submitted = Assert.Single(pipe.Submitted);
        var record    = Assert.Single(submitted);
        Assert.Equal(CollectionRecordTypes.FacePhoto, record.RecordType);

        var payload = record.Payload.Deserialize<FacePhotoPayload>()!;
        Assert.NotNull(payload.Latitude);
        Assert.NotNull(payload.Longitude);
        Assert.InRange(payload.Latitude!.Value,  13.08, 13.09);
        Assert.InRange(payload.Longitude!.Value, 80.27, 80.28);
        Assert.Equal("Chennai Office", payload.LocationAddress);
    }

    [Fact]
    public async Task Continue_ClockinContext_SendsLifecycleClockInBeforeSubmittingPhoto()
    {
        PhotoCaptureWindowViewModel.IdentityVerificationDwell = TimeSpan.Zero;
        var pipe = new FakeNamedPipeClient();
        var vm   = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["validate", "lifecycle:ClockIn", "submit"], pipe.CallOrder);
        Assert.Single(pipe.Submitted);
    }

    [Fact]
    public async Task Continue_ClockinContext_DoesNotClockInWhenQualityFails()
    {
        PhotoCaptureWindowViewModel.IdentityVerificationDwell = TimeSpan.Zero;
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, true, true, false, false, null, "poor_lighting")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["validate"], pipe.CallOrder);
        Assert.Empty(pipe.Submitted);
        Assert.False(vm.LightingPassed);
        Assert.Contains("Lighting", vm.CaptureStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Continue_ClockinContext_DoesNotClockInWhenSunglassesDetected()
    {
        PhotoCaptureWindowViewModel.IdentityVerificationDwell = TimeSpan.Zero;
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, true, false, false, false, null, "sunglasses_or_mask")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["validate"], pipe.CallOrder);
        Assert.Empty(pipe.Submitted);
        Assert.True(vm.NoObstructionFailed);
        Assert.Contains("sunglasses", vm.CaptureStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CapturePhoto_ResetsValidationChecks()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, false, false, false, false, null, "face_not_visible")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.True(vm.HasValidationResult);

        pipe.NextFacePhotoValidateResult = null;
        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.LightingPassed);
        Assert.True(vm.FaceVisiblePassed);
        Assert.True(vm.NoObstructionPassed);
    }

    [Fact]
    public async Task Continue_Enrollment_UsesAwsChecksBeforeSavingFace()
    {
        var prefs = new FakePreferencesStore();
        var pipe = new FakeNamedPipeClient();
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, prefs, new CapturedPhotoBuffer());

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["validate", "submit"], pipe.CallOrder);
        Assert.True(vm.LightingPassed);
        Assert.True(vm.FaceVisiblePassed);
        Assert.True(vm.NoObstructionPassed);
        Assert.Equal("true", prefs.Get(SessionPreferenceKeys.FaceVerified, ""));
    }

    [Fact]
    public async Task Continue_Enrollment_DoesNotSaveFaceWhenAwsRejectsLighting()
    {
        var prefs = new FakePreferencesStore();
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, true, true, false, false, null, "poor_lighting")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, prefs, new CapturedPhotoBuffer());

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(["validate"], pipe.CallOrder);
        Assert.Empty(pipe.Submitted);
        Assert.False(vm.LightingPassed);
        Assert.True(vm.FaceVisiblePassed);
        Assert.True(vm.NoObstructionPassed);
        Assert.Equal("", prefs.Get(SessionPreferenceKeys.FaceVerified, ""));
    }

    [Fact]
    public async Task Continue_ClockinContext_DoesNotSubmitPhotoWhenLifecycleFails()
    {
        PhotoCaptureWindowViewModel.IdentityVerificationDwell = TimeSpan.Zero;
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(false, "device_locked", "Device is locked.", MonitoringState.Stopped, null)
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Empty(pipe.Submitted);
        Assert.Equal("Device is locked.", vm.CaptureStatusText);
        Assert.True(vm.IsVerificationFailed);
        Assert.Equal(PhotoCaptureWindowViewModel.TryAgainLabel, vm.PrimaryButtonLabel);
        Assert.True(vm.PrimaryActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task AwsRejection_ShowsTryAgainInsteadOfVerifyLabel()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, false, true, false, false, null, "face_not_visible")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.IsVerificationFailed);
        Assert.False(vm.ShowCapturedSuccess);
        Assert.True(vm.ShowStatusBelow);
        Assert.Equal("Try again", vm.PrimaryButtonLabel);
    }

    [Fact]
    public async Task TryAgain_ReturnsToLiveCamera_ThenCaptureRechecks()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, false, true, false, false, null, "face_not_visible")
        };
        var camera = new FakeCameraService { ShouldReturnPhoto = true };
        var vm = new PhotoCaptureWindowViewModel(camera, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");
        await vm.CapturePhotoCommand.ExecuteAsync(null);

        // "Try again" only goes back to the live feed — no new photo yet.
        await vm.PrimaryActionCommand.ExecuteAsync(null);

        Assert.Equal(1, camera.CallCount);
        Assert.False(vm.IsCaptured);
        Assert.False(vm.IsVerificationFailed);
        Assert.False(vm.HasValidationResult);
        Assert.Null(vm.CapturedPhotoBytes);
        Assert.Equal(PhotoCaptureWindowViewModel.CaptureLabel, vm.PrimaryButtonLabel);
        Assert.Equal(PhotoCaptureWindowViewModel.DefaultPrompt, vm.CaptureStatusText);

        pipe.NextFacePhotoValidateResult = null;
        await vm.PrimaryActionCommand.ExecuteAsync(null);

        Assert.Equal(2, camera.CallCount);
        Assert.Equal(["validate", "validate"], pipe.CallOrder);
        Assert.True(vm.ShowCapturedSuccess);
        Assert.Equal("Verify & Clock In", vm.PrimaryButtonLabel);
    }

    [Theory]
    [InlineData("clockin", "//clockin")]
    [InlineData("clockout", "//active")]
    [InlineData(null, "//review")]
    public void Back_ReturnsToScreenThatOpenedCapture(string? context, string expected)
    {
        var vm = MakeVm();
        vm.SetContext(context);

        Assert.Equal(expected, vm.BackRoute);
        Assert.True(vm.BackCommand.CanExecute(null));
    }

    [Fact]
    public void BeforeCapture_PrimaryButtonIsCapture()
    {
        var vm = MakeVm();
        vm.SetContext("clockin");

        Assert.Equal(PhotoCaptureWindowViewModel.CaptureLabel, vm.PrimaryButtonLabel);
        Assert.True(vm.PrimaryActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task NoFaceDetected_OnlyFaceCheckFails_OthersStayNeutral()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, false, false, false, false, null, "no_face_detected")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.FaceVisibleFailed);
        Assert.False(vm.LightingPassed);
        Assert.False(vm.LightingFailed);
        Assert.False(vm.NoObstructionPassed);
        Assert.False(vm.NoObstructionFailed);
        Assert.False(vm.MatchPassed);
        Assert.False(vm.MatchFailed);
        Assert.Contains("No face detected", vm.CaptureStatusText);
    }

    [Fact]
    public async Task FaceHalfInFrame_WellLitRoom_OnlyFaceIsRed_LightingGreen_MaskGrey()
    {
        // What AWS returns when only the top of the head is in frame: every flag false.
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, false, false, false, false, null, "face_not_visible")
        };
        var camera = new FakeCameraService
        {
            PhotoBytes = ONEVO.Agent.TrayApp.Tests.Capture.PhotoBrightnessTests.SolidJpeg(
                System.Drawing.Color.FromArgb(150, 140, 130))
        };
        var vm = new PhotoCaptureWindowViewModel(camera, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.FaceVisibleFailed);
        Assert.True(vm.LightingPassed);
        Assert.False(vm.LightingFailed);
        Assert.False(vm.NoObstructionPassed);
        Assert.False(vm.NoObstructionFailed);
        Assert.False(vm.MatchFailed);
        Assert.Contains("not fully in the frame", vm.CaptureStatusText);
        Assert.DoesNotContain("brighter", vm.CaptureStatusText);
        Assert.Equal(PhotoCaptureWindowViewModel.TryAgainLabel, vm.PrimaryButtonLabel);
    }

    [Fact]
    public async Task FaceHalfInFrame_DarkRoom_LightingRedToo()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, false, false, false, false, null, "face_not_visible")
        };
        var camera = new FakeCameraService
        {
            PhotoBytes = ONEVO.Agent.TrayApp.Tests.Capture.PhotoBrightnessTests.SolidJpeg(
                System.Drawing.Color.FromArgb(15, 15, 15))
        };
        var vm = new PhotoCaptureWindowViewModel(camera, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.FaceVisibleFailed);
        Assert.True(vm.LightingFailed);
        Assert.False(vm.NoObstructionFailed);
        Assert.Contains("brighter", vm.CaptureStatusText);
    }

    [Fact]
    public async Task ClearFace_WithSunglasses_OnlyMaskIsRed()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, true, false, false, false, null, "sunglasses_or_mask")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.LightingPassed);
        Assert.True(vm.FaceVisiblePassed);
        Assert.True(vm.NoObstructionFailed);
        Assert.False(vm.MatchFailed);
    }

    [Fact]
    public async Task MultipleFaces_ShowsOnePersonMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, false, false, false, false, false, null, "multiple_faces")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.Contains("Only one person", vm.CaptureStatusText);
        Assert.False(vm.LightingFailed);
    }

    [Fact]
    public async Task DifferentPerson_MatchRowFails_QualityRowsPass()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, true, true, false, false, 12f, "not_matched")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.True(vm.ShowMatchCheck);
        Assert.True(vm.MatchFailed);
        Assert.True(vm.LightingPassed);
        Assert.True(vm.FaceVisiblePassed);
        Assert.True(vm.NoObstructionPassed);
        Assert.Contains("did not match", vm.CaptureStatusText);
        Assert.Equal(["validate"], pipe.CallOrder);
        Assert.Equal(PhotoCaptureWindowViewModel.TryAgainLabel, vm.PrimaryButtonLabel);
    }

    [Fact]
    public async Task ClockIn_WithNoReference_BlocksWithoutInPlaceEnrollment()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextFacePhotoValidateResult = new FacePhotoValidateResultPayload(
                true, null, true, true, true, false, false, null, "no_reference_photo")
        };
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext("clockin");

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.True(vm.NeedsFaceSetup);
        Assert.False(vm.MatchFailed);
        Assert.Equal(PhotoCaptureWindowViewModel.FaceSetupRequiredLabel, vm.PrimaryButtonLabel);
        Assert.Contains("Contact HR", vm.CaptureStatusText);
        // No shortcut into enrollment from clock-in: whoever is at the laptop must not enrol their face.
        Assert.False(vm.PrimaryActionCommand.CanExecute(null));
        Assert.Equal("Verify Your Identity", vm.Headline);
        Assert.Equal([FacePhotoValidatePurposes.ClockIn], pipe.ValidatePurposes);
    }

    [Theory]
    [InlineData("clockin", FacePhotoValidatePurposes.ClockIn)]
    [InlineData("clockout", FacePhotoValidatePurposes.ClockOut)]
    [InlineData(null, FacePhotoValidatePurposes.Enrollment)]
    public async Task Validate_SendsPurposeForContext(string? context, string expected)
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new PhotoCaptureWindowViewModel(
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, new FakePreferencesStore(), new CapturedPhotoBuffer());
        vm.SetContext(context);

        await vm.CapturePhotoCommand.ExecuteAsync(null);

        Assert.Equal([expected], pipe.ValidatePurposes);
    }
}
