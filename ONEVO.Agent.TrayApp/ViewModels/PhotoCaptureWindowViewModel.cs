namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isCaptured;

    [ObservableProperty] private bool   _isCapturing;
    [ObservableProperty] private bool   _isScanAnimating;
    [ObservableProperty] private string _captureStatusText =
        "Look at the camera and keep your face within the frame.";

    public PhotoCaptureWindowViewModel(ICameraService camera)
    {
        Title   = "Face Verification";
        _camera = camera;
    }

    [RelayCommand]
    private async Task CapturePhotoAsync(CancellationToken ct)
    {
        IsCapturing      = true;
        IsScanAnimating  = true;
        CaptureStatusText = "Scanning your face...";
        try
        {
            var bytes = await _camera.CapturePhotoAsync(ct);
            IsCaptured        = bytes is { Length: > 0 };
            CaptureStatusText = IsCaptured
                ? "Face captured! Click Continue to proceed."
                : "No photo taken. Please try again.";
        }
        catch
        {
            IsCaptured        = false;
            CaptureStatusText = "Camera error. Please try again.";
        }
        finally
        {
            IsCapturing     = false;
            IsScanAnimating = false;
        }
    }

    private bool CanContinue => IsCaptured;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private static void Continue()
    {
        // Navigation wired in code-behind
    }
}
