using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PhotoCaptureWindowViewModelTests
{
    private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true) =>
        new(new FakeCameraService { ShouldReturnPhoto = cameraSucceeds });

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
        var vm   = new PhotoCaptureWindowViewModel(fake);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.Equal(1, fake.CallCount);
    }
}
