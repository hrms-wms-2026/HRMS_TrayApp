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

        Assert.Equal(["lifecycle:ClockIn", "submit"], pipe.CallOrder);
        Assert.Single(pipe.Submitted);
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
    }
}
