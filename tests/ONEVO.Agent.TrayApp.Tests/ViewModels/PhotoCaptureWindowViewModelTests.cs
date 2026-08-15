using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PhotoCaptureWindowViewModelTests
{
    private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true) =>
        new(new FakeCameraService { ShouldReturnPhoto = cameraSucceeds },
            new FakeNamedPipeClient(),
            new FakePreferencesStore());

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
        var vm   = new PhotoCaptureWindowViewModel(fake, new FakeNamedPipeClient(), new FakePreferencesStore());
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
            new FakeCameraService { ShouldReturnPhoto = true }, pipe, prefs);

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
}
