namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _prefs;
    private readonly CapturedPhotoBuffer _photoBuffer;
    private readonly ILogger<PhotoCaptureWindowViewModel> _logger;
    private byte[]? _capturedBytes;
    private string? _captureContext;

    public const string DefaultPrompt =
        "Look at the camera and keep your face within the frame.";

    /// <summary>How long the "Verify Your Identity" screen stays up before clock-in finishes.</summary>
    public static TimeSpan IdentityVerificationDwell { get; set; } = TimeSpan.FromMilliseconds(1600);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isCaptured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isCapturing;

    [ObservableProperty] private bool    _isScanAnimating;
    [ObservableProperty] private object? _previewFrameSource;
    [ObservableProperty] private byte[]? _capturedPhotoBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private string _captureStatusText = DefaultPrompt;

    /// <summary>Hides the duplicate hint under the circle until capture/scan/error changes it.</summary>
    public bool ShowStatusBelow =>
        IsCapturing ||
        IsCaptured ||
        !string.Equals(CaptureStatusText, DefaultPrompt, StringComparison.Ordinal);

    public PhotoCaptureWindowViewModel(
        ICameraService camera,
        INamedPipeClient pipe,
        IPreferencesStore prefs,
        CapturedPhotoBuffer photoBuffer,
        ILogger<PhotoCaptureWindowViewModel>? logger = null)
    {
        Title        = "Face Verification";
        _camera      = camera;
        _pipe        = pipe;
        _prefs       = prefs;
        _photoBuffer = photoBuffer;
        _logger      = logger ?? NullLoggerFactory.Instance.CreateLogger<PhotoCaptureWindowViewModel>();
    }

    /// <summary>
    /// Called by the page when a Shell query parameter is received.
    /// Pass "clockin" to complete clock-in after face capture.
    /// </summary>
    public void SetContext(string? context)
    {
        _captureContext   = context;
        _capturedBytes    = null;
        CapturedPhotoBytes = null;
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
            CapturedPhotoBytes = _capturedBytes;
            IsCaptured        = _capturedBytes is not null;
            CaptureStatusText = IsCaptured
                ? "Face captured successfully."
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
        if (_captureContext == "clockin")
        {
            // Show the "Verify Your Identity" match screen with the just-captured selfie
            // before finishing clock-in. Purely a visual dwell step — the actual match
            // decision still happens server-side; nothing here blocks on a real result.
            _photoBuffer.Bytes = _capturedBytes;
            try { await Shell.Current.GoToAsync("identity-verification"); } catch { /* unit tests */ }
            await Task.Delay(IdentityVerificationDwell);

            // Clock-in must land first: the Service only accepts collection-record submits
            // (including this face photo) while monitoring is Active, and ClockIn is what
            // transitions it there. Submitting the photo before this would always be rejected.
            CaptureStatusText = "Completing clock-in...";
            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);
            if (result is null || !result.Success)
            {
                CaptureStatusText = result?.Message ?? result?.ErrorCode ?? "Clock-in failed. Please try again.";
                IsCaptured = false;
                _capturedBytes = null;
                // Back to the capture screen so the failure message is visible — navigating
                // without the "context" query param avoids re-triggering SetContext, which
                // would otherwise wipe the message we just set.
                try { await Shell.Current.GoToAsync(".."); } catch { /* unit tests */ }
                return;
            }

            await SubmitFacePhotoRecordAsync();

            try { await Shell.Current.GoToAsync("//active"); }
            catch { /* unit tests */ }
            return;
        }

        await SubmitFacePhotoRecordAsync();

        try { Preferences.Set("onevo.face_verified", true); }
        catch { /* no MAUI Preferences host in unit tests */ }
        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
    }

    private async Task SubmitFacePhotoRecordAsync()
    {
        if (_capturedBytes is not { Length: > 0 }) return;

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
        catch (Exception ex)
        {
            // Non-blocking by design — a photo send failure should not trap the employee mid
            // clock-in/enrollment flow — but it must not vanish silently either.
            _logger.LogWarning(ex, "Face photo submit failed");
        }
    }
}
