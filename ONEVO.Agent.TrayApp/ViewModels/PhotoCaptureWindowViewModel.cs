namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _prefs;
    private byte[]? _capturedBytes;
    private string? _captureContext;

    public const string DefaultPrompt =
        "Look at the camera and keep your face within the frame.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isCaptured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isCapturing;

    [ObservableProperty] private bool    _isScanAnimating;
    [ObservableProperty] private object? _previewFrameSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private string _captureStatusText = DefaultPrompt;

    /// <summary>Hides the duplicate hint under the circle until capture/scan/error changes it.</summary>
    public bool ShowStatusBelow =>
        IsCapturing ||
        IsCaptured ||
        !string.Equals(CaptureStatusText, DefaultPrompt, StringComparison.Ordinal);

    public PhotoCaptureWindowViewModel(ICameraService camera, INamedPipeClient pipe, IPreferencesStore prefs)
    {
        Title   = "Face Verification";
        _camera = camera;
        _pipe   = pipe;
        _prefs  = prefs;
    }

    /// <summary>
    /// Called by the page when a Shell query parameter is received.
    /// Pass "clockin" to complete clock-in after face capture.
    /// </summary>
    public void SetContext(string? context)
    {
        _captureContext   = context;
        _capturedBytes    = null;
        IsCaptured        = false;
        CaptureStatusText = DefaultPrompt;
    }

    public async Task StartPreviewAsync()
    {
        PreviewFrameSource = await _camera.StartPreviewAsync();
        IsScanAnimating = true;
    }

    public async Task StopPreviewAsync()
    {
        IsScanAnimating = false;
        PreviewFrameSource = null; // signals handler to release MediaPlayer first
        await _camera.StopPreviewAsync();
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
                double? lat = double.TryParse(
                    _prefs.Get("onevo.live_latitude", ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var la) ? la : null;
                double? lon = double.TryParse(
                    _prefs.Get("onevo.live_longitude", ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var lo) ? lo : null;
                var locationDisplay = _prefs.Get("onevo.work_location_display", "");

                var payload = new FacePhotoPayload
                {
                    Format          = "jpeg",
                    Data            = Convert.ToBase64String(_capturedBytes),
                    Latitude        = lat,
                    Longitude       = lon,
                    LocationAddress = string.IsNullOrEmpty(locationDisplay) ? null : locationDisplay
                };
                var record = new CollectionRecord
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

        try { Preferences.Set("onevo.face_verified", true); }
        catch { /* no MAUI Preferences host in unit tests */ }
        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
    }
}
