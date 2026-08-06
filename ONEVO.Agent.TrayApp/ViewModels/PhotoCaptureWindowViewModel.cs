namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;
    private readonly INamedPipeClient _pipe;
    private byte[]? _capturedBytes;

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

        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
    }
}
