namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.IPC;
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
    [NotifyPropertyChangedFor(nameof(ShowLiveFrame))]
    [NotifyPropertyChangedFor(nameof(ShowCameraFallback))]
    private bool _isCaptured;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isValidating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightingPassed))]
    [NotifyPropertyChangedFor(nameof(LightingFailed))]
    [NotifyPropertyChangedFor(nameof(FaceVisiblePassed))]
    [NotifyPropertyChangedFor(nameof(FaceVisibleFailed))]
    [NotifyPropertyChangedFor(nameof(NoObstructionPassed))]
    [NotifyPropertyChangedFor(nameof(NoObstructionFailed))]
    private bool _hasValidationResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightingPassed))]
    [NotifyPropertyChangedFor(nameof(LightingFailed))]
    private bool _lightingOk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaceVisiblePassed))]
    [NotifyPropertyChangedFor(nameof(FaceVisibleFailed))]
    private bool _faceVisibleOk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoObstructionPassed))]
    [NotifyPropertyChangedFor(nameof(NoObstructionFailed))]
    private bool _noObstructionOk;

    public bool LightingPassed => HasValidationResult && LightingOk;
    public bool LightingFailed => HasValidationResult && !LightingOk;
    public bool FaceVisiblePassed => HasValidationResult && FaceVisibleOk;
    public bool FaceVisibleFailed => HasValidationResult && !FaceVisibleOk;
    public bool NoObstructionPassed => HasValidationResult && NoObstructionOk;
    public bool NoObstructionFailed => HasValidationResult && !NoObstructionOk;

    [ObservableProperty] private bool    _isScanAnimating;
    [ObservableProperty] private object? _previewFrameSource;
    [ObservableProperty] private byte[]? _capturedPhotoBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLiveFrame))]
    [NotifyPropertyChangedFor(nameof(ShowCameraFallback))]
    private byte[]? _livePreviewBytes;

    public bool ShowLiveFrame => !IsCaptured && LivePreviewBytes is { Length: > 0 };
    public bool ShowCameraFallback => !IsCaptured && LivePreviewBytes is not { Length: > 0 };

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
        LoadEmployee();
    }

    private void LoadEmployee()
    {
        EmployeeName = SetupFlow.DisplayOrDash(EmployeeSession.Name(_prefs));
        EmployeeId = SetupFlow.DisplayOrDash(EmployeeSession.Id(_prefs));
    }

    /// <summary>
    /// Called by the page when a Shell query parameter is received.
    /// Pass "clockin" to complete clock-in after face capture.
    /// </summary>
    [ObservableProperty] private string _headline = "Set Up Face Verification";
    [ObservableProperty] private string _contextPill = "Identity Enrolment";
    [ObservableProperty] private string _continueLabel = "Enroll & Continue";
    [ObservableProperty] private string _employeeName = "—";
    [ObservableProperty] private string _employeeId = "—";

    public void SetContext(string? context)
    {
        _captureContext   = context;
        _capturedBytes    = null;
        CapturedPhotoBytes = null;
        IsCaptured        = false;
        CaptureStatusText = DefaultPrompt;
        ResetValidation();
        LoadEmployee();
        if (string.Equals(context, "clockin", StringComparison.OrdinalIgnoreCase))
        {
            Headline = "Verify Your Identity";
            ContextPill = "Clock-in verification";
            ContinueLabel = "Verify & Clock In";
        }
        else if (string.Equals(context, "clockout", StringComparison.OrdinalIgnoreCase))
        {
            Headline = "Verify Your Identity";
            ContextPill = "Clock-out verification";
            ContinueLabel = "Verify & Clock Out";
        }
        else
        {
            Headline = "Set Up Face Verification";
            ContextPill = "Identity Enrolment";
            ContinueLabel = "Enroll & Continue";
        }
    }

    public async Task StartPreviewAsync()
    {
        _camera.PreviewFrame -= OnPreviewFrame;
        _camera.PreviewFrame += OnPreviewFrame;
        PreviewFrameSource = await _camera.StartPreviewAsync();
        // The sweep reads as a cut across the mouth while the employee is lining up.
        IsScanAnimating = false;
    }

    public async Task StopPreviewAsync()
    {
        _camera.PreviewFrame -= OnPreviewFrame;
        IsScanAnimating = false;
        LivePreviewBytes = null;
        PreviewFrameSource = null; // signals handler to release MediaPlayer first
        await _camera.StopPreviewAsync();
    }

    private void OnPreviewFrame(object? sender, byte[] jpeg)
    {
        if (IsCaptured || jpeg is not { Length: > 0 })
            return;

        LivePreviewBytes = jpeg;
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
            ResetValidation();
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

        if (IsCaptured)
            await TryValidateCapturedPhotoAsync();
    }

    private bool CanContinue => IsCaptured && !IsValidating;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        if (_captureContext == "clockout")
        {
            if (!await EnsureAwsQualityPassedAsync())
                return;

            CaptureStatusText = "Completing clock-out...";
            var clockOut = await _pipe.SendLifecycleAsync(LifecycleAction.ClockOut, CancellationToken.None);
            if (clockOut is null || !clockOut.Success)
            {
                CaptureStatusText = clockOut?.Message ?? clockOut?.ErrorCode ?? "Clock-out failed. Please try again.";
                IsCaptured = false;
                return;
            }

            await SubmitFacePhotoRecordAsync();
            try { await Shell.Current.GoToAsync("//end"); }
            catch { /* unit tests */ }
            return;
        }

        if (_captureContext == "clockin")
        {
            if (!await EnsureAwsQualityPassedAsync())
                return;

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

        if (!await EnsureAwsQualityPassedAsync())
            return;

        await SubmitFacePhotoRecordAsync();

        try { Preferences.Set("onevo.face_verified", true); }
        catch { /* no MAUI Preferences host in unit tests */ }
        _prefs.Set(SessionPreferenceKeys.FaceVerified, "true");
        try
        {
            await Shell.Current.GoToAsync(
                SetupFlow.AfterFaceEnrollment(_pipe.LastKnownPolicy?.AllowsDailyLocationChoice ?? false));
        }
        catch { /* unit tests */ }
    }

    private void ResetValidation()
    {
        HasValidationResult = false;
        LightingOk = false;
        FaceVisibleOk = false;
        NoObstructionOk = false;
        _photoBuffer.LastValidation = null;
    }

    /// <summary>
    /// Enrollment and clock-in share one AWS Rekognition result.
    /// A failed check stays on screen until the employee retakes.
    /// </summary>
    private async Task<bool> EnsureAwsQualityPassedAsync()
    {
        if (_photoBuffer.LastValidation is { CanProceed: true })
            return true;

        if (_photoBuffer.LastValidation is not null)
            return false;

        return await TryValidateCapturedPhotoAsync();
    }

    private async Task<bool> TryValidateCapturedPhotoAsync()
    {
        if (_capturedBytes is not { Length: > 0 })
        {
            CaptureStatusText = "No photo taken. Please try again.";
            return false;
        }

        IsValidating = true;
        CaptureStatusText = "Checking lighting and face...";
        try
        {
            var result = await _pipe.ValidateFacePhotoAsync("jpeg", _capturedBytes, CancellationToken.None);
            ApplyValidation(result);
            if (result is { Success: true, CanProceed: true })
            {
                CaptureStatusText = "Face captured successfully.";
                return true;
            }

            CaptureStatusText = BuildRetakeMessage(result);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face photo validate failed");
            ApplyValidation(null);
            CaptureStatusText = "Face verification is unavailable. Retake and try again.";
            return false;
        }
        finally
        {
            IsValidating = false;
        }
    }

    private void ApplyValidation(FacePhotoValidateResultPayload? result)
    {
        HasValidationResult = result is not null;
        LightingOk = result?.LightingOk == true;
        FaceVisibleOk = result?.FaceVisible == true;
        NoObstructionOk = result?.NoSunglassesOrMask == true;
        _photoBuffer.LastValidation = result;
    }

    internal static string BuildRetakeMessage(FacePhotoValidateResultPayload? result)
    {
        if (result is null || result.Success == false)
            return "Face verification is unavailable. Retake and try again.";

        if (!result.FaceVisible)
            return "Face is not clearly visible. Look at the camera and retake.";
        if (!result.LightingOk)
            return "Lighting is too low. Move to a brighter spot and retake.";
        if (!result.NoSunglassesOrMask)
            return "Remove sunglasses or mask and retake.";
        if (result.FailureReason == "no_reference_photo")
            return "No enrolled face photo. Complete face setup, then try again.";
        if (result.FailureReason == "verification_failed")
            return "Face check reached AWS but could not finish. Try again.";
        if (!result.IsMatch)
            return "Face did not match. Look at the camera and retake.";
        return "Retake photo and try again.";
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
