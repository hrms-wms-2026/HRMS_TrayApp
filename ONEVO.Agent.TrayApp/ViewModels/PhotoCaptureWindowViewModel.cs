namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;
    private readonly INamedPipeClient _pipe;
    private byte[]? _capturedBytes;
    private string? _captureContext;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isCaptured;

    [ObservableProperty] private bool   _isCapturing;
    [ObservableProperty] private bool   _isScanAnimating;
    [ObservableProperty] private string _captureStatusText =
        "Look at the camera and keep your face within the frame.";

    public PhotoCaptureWindowViewModel(ICameraService camera, INamedPipeClient pipe)
    {
        Title   = "Face Verification";
        _camera = camera;
        _pipe   = pipe;
    }

    /// <summary>
    /// Called by the page when a Shell query parameter is received.
    /// Pass "clockin" to complete clock-in after face capture.
    /// </summary>
    public void SetContext(string? context)
    {
        _captureContext = context;
        // Reset state so each entry starts clean.
        _capturedBytes    = null;
        IsCaptured        = false;
        CaptureStatusText = "Look at the camera and keep your face within the frame.";
    }

    [RelayCommand]
    private async Task CapturePhotoAsync(CancellationToken ct)
    {
        IsCapturing       = true;
        IsScanAnimating   = true;
        CaptureStatusText = "Scanning your face...";
        try
        {
            var bytes = await _camera.CapturePhotoAsync(ct);
            _capturedBytes    = bytes is { Length: > 0 } ? bytes : null;
            IsCaptured        = _capturedBytes is not null;
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
    private async Task Continue()
    {
        if (_capturedBytes is { Length: > 0 })
        {
            try
            {
                var payload = new { format = "jpeg", data = Convert.ToBase64String(_capturedBytes) };
                var record  = new CollectionRecord
                {
                    EventId          = Guid.NewGuid().ToString("N"),
                    RecordType       = CollectionRecordTypes.FacePhoto,
                    SchemaVersion    = CollectionSchemaVersions.FacePhotoV1,
                    CaptureTimestamp = DateTimeOffset.UtcNow,
                    DeviceId         = Environment.MachineName,
                    Payload          = JsonSerializer.SerializeToElement(payload)
                };
                await _pipe.SubmitCollectionRecordsAsync([record], CancellationToken.None);
            }
            catch { /* non-blocking — photo send failure should not block navigation */ }
        }

        if (_captureContext == "clockin")
        {
            CaptureStatusText = "Completing clock-in...";
            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);
            if (result is null || !result.Success)
            {
                CaptureStatusText = result?.Message ?? result?.ErrorCode ?? "Clock-in failed. Please try again.";
                IsCaptured = false;
                _capturedBytes = null;
                return;
            }

            try { await Shell.Current.GoToAsync("//active"); }
            catch { /* unit tests */ }
            return;
        }

        Preferences.Set("onevo.face_verified", true);
        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
    }
}
