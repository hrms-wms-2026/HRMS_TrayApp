namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Capture;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Face capture for clock-in, clock-out (one photo, matched against the enrolled face) and
/// face setup (three photos — look straight, turn left, turn right — saved together).
/// </summary>
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
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    [NotifyPropertyChangedFor(nameof(ShowLiveFrame))]
    [NotifyPropertyChangedFor(nameof(ShowCameraFallback))]
    [NotifyPropertyChangedFor(nameof(ShowCapturedSuccess))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonLabel))]
    private bool _isCaptured;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    [NotifyPropertyChangedFor(nameof(ShowCapturedSuccess))]
    [NotifyPropertyChangedFor(nameof(CanUseCameraButton))]
    private bool _isValidating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    [NotifyPropertyChangedFor(nameof(CanUseCameraButton))]
    private bool _isCapturing;

    /// <summary>
    /// True when the AWS face check (or the clock-in/out / face setup save after it) failed. The
    /// status row turns red and the primary button becomes "Try again".
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryActionCommand))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonLabel))]
    [NotifyPropertyChangedFor(nameof(NeedsFaceSetup))]
    [NotifyPropertyChangedFor(nameof(ShowCapturedSuccess))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBelow))]
    private bool _isVerificationFailed;

    public const string TryAgainLabel = "Try again";
    public const string CaptureLabel = "Capture";
    public const string FaceSetupRequiredLabel = "Face setup required";
    public const string ContactHrLabel = "Contact HR";

    /// <summary>
    /// Clock-in/out found no enrolled face. Retaking can never fix that, and offering an
    /// in-place "set up face" shortcut would let whoever is at the laptop enrol their own face,
    /// so the button is disabled and the employee is told to go through HR.
    /// </summary>
    public bool NeedsFaceSetup =>
        IsVerificationFailed && !IsEnrollment && FailureReason == FaceCheckFailureCodes.NoReferencePhoto;

    public string PrimaryButtonLabel =>
        NeedsFaceSetup ? FaceSetupRequiredLabel
        : _enrollmentBlocked ? ContactHrLabel
        : IsVerificationFailed || _retryCommit ? TryAgainLabel
        : IsEnrollment ? (SetupComplete ? ContinueLabel : CaptureLabel)
        : !IsCaptured ? CaptureLabel
        : ContinueLabel;

    /// <summary>Green "captured" pill — only while the photo is captured and not in an error state.</summary>
    public bool ShowCapturedSuccess => IsCaptured && !IsValidating && !IsVerificationFailed;

    /// <summary>Last AWS result. Every checklist state below is derived from it.</summary>
    private FacePhotoValidateResultPayload? _validation;

    public bool HasValidationResult => _validation is not null;
    private string? FailureReason => _validation?.FailureReason;

    /// <summary>AWS actually judged this photo (not unavailable / errored).</summary>
    private bool Evaluated => _validation is { Success: true } && FailureReason != FaceCheckFailureCodes.VerificationFailed;

    /// <summary>
    /// No face, several faces, or a face only partly in frame. AWS's per-face lighting and
    /// sunglasses/mask flags are unreliable then (a cut-off face reads as "occluded", hair as
    /// "dark"), so only the face check is shown as the problem.
    /// </summary>
    private bool FaceProblem =>
        Evaluated
        && (FailureReason is FaceCheckFailureCodes.NoFaceDetected
                or FaceCheckFailureCodes.MultipleFaces
                or FaceCheckFailureCodes.FaceNotVisible
            || !_validation!.FaceVisible);

    /// <summary>Whole-photo brightness, standing in for AWS lighting when <see cref="FaceProblem"/>.</summary>
    private bool? _photoLightingOk;

    public bool LightingPassed => Evaluated && (FaceProblem ? _photoLightingOk == true : _validation!.LightingOk);
    public bool LightingFailed => Evaluated && (FaceProblem ? _photoLightingOk == false : !_validation!.LightingOk);
    public bool FaceVisiblePassed => Evaluated && !FaceProblem;
    public bool FaceVisibleFailed => FaceProblem;
    // Sunglasses/mask cannot be judged without a clear face — stays grey, never a false red.
    public bool NoObstructionPassed => Evaluated && !FaceProblem && _validation!.NoSunglassesOrMask;
    public bool NoObstructionFailed => Evaluated && !FaceProblem && !_validation!.NoSunglassesOrMask;

    /// <summary>The "Face matches" row only makes sense when comparing against an enrolled face.</summary>
    public bool ShowMatchCheck => !IsEnrollment;
    public bool MatchPassed => Evaluated && _validation!.IsMatch && _validation.CanProceed;
    public bool MatchFailed => Evaluated && FailureReason == FaceCheckFailureCodes.NotMatched;

    private static readonly string[] ValidationDerivedProperties =
    [
        nameof(HasValidationResult),
        nameof(LightingPassed), nameof(LightingFailed),
        nameof(FaceVisiblePassed), nameof(FaceVisibleFailed),
        nameof(NoObstructionPassed), nameof(NoObstructionFailed),
        nameof(MatchPassed), nameof(MatchFailed),
        nameof(NeedsFaceSetup), nameof(PrimaryButtonLabel)
    ];

    private void SetValidation(FacePhotoValidateResultPayload? result)
    {
        _validation = result;
        _photoBuffer.LastValidation = result;
        _photoLightingOk = FaceProblem ? PhotoBrightness.IsOk(_capturedBytes) : null;
        foreach (var name in ValidationDerivedProperties)
            OnPropertyChanged(name);
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private bool IsEnrollment =>
        !string.Equals(_captureContext, "clockin", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(_captureContext, "clockout", StringComparison.OrdinalIgnoreCase);

    private string ValidatePurpose =>
        string.Equals(_captureContext, "clockin", StringComparison.OrdinalIgnoreCase) ? FacePhotoValidatePurposes.ClockIn
        : string.Equals(_captureContext, "clockout", StringComparison.OrdinalIgnoreCase) ? FacePhotoValidatePurposes.ClockOut
        : FacePhotoValidatePurposes.Enrollment;

    // ── Face setup (enrollment): three photos ────────────────────────────────────────────────

    private const int SetupPhotoCount = 3;

    /// <summary>One id per face setup attempt; the Service keeps passing photos under it.</summary>
    private Guid _enrollmentSessionId = Guid.NewGuid();

    private readonly byte[]?[] _setupPhotos = new byte[]?[SetupPhotoCount];

    /// <summary>Employee was already enrolled and the front photo matched — side photos are skipped.</summary>
    private bool _alreadyEnrolled;

    /// <summary>A different face is already on file (or appeared meanwhile) — only HR can change it.</summary>
    private bool _enrollmentBlocked;

    /// <summary>Saving the three photos failed for a reason retaking cannot fix (Service/backend down).</summary>
    private bool _retryCommit;

    /// <summary>Index of the face setup step being captured: 0 front, 1 left, 2 right, 3 = all done.</summary>
    [ObservableProperty] private int _setupStep;

    public bool ShowSetupSteps => IsEnrollment;
    public byte[]? FrontSetupPhoto => _setupPhotos[0];
    public byte[]? LeftSetupPhoto => _setupPhotos[1];
    public byte[]? RightSetupPhoto => _setupPhotos[2];
    public bool FrontSetupDone => _setupPhotos[0] is not null;
    public bool LeftSetupDone => _setupPhotos[1] is not null || _alreadyEnrolled;
    public bool RightSetupDone => _setupPhotos[2] is not null || _alreadyEnrolled;
    public bool IsFrontStepActive => IsEnrollment && !SetupComplete && SetupStep == 0;
    public bool IsLeftStepActive => IsEnrollment && !SetupComplete && SetupStep == 1;
    public bool IsRightStepActive => IsEnrollment && !SetupComplete && SetupStep == 2;

    public bool SetupComplete =>
        IsEnrollment && (_alreadyEnrolled || _setupPhotos.All(p => p is not null));

    public string CurrentSetupPose => FaceSetupPoses.All[Math.Clamp(SetupStep, 0, SetupPhotoCount - 1)];

    /// <summary>The line under the employee name: what to do right now.</summary>
    public string InstructionText =>
        !IsEnrollment ? DefaultPrompt
        : _alreadyEnrolled ? "Your face is already set up."
        : SetupComplete ? "All 3 photos taken."
        : SetupStep switch
        {
            0 => "Step 1 of 3 · Look straight at the camera.",
            1 => "Step 2 of 3 · Turn your head slightly to the left.",
            _ => "Step 3 of 3 · Turn your head slightly to the right."
        };

    /// <summary>The small camera button: not while busy, and not once all setup photos are in.</summary>
    public bool CanUseCameraButton =>
        !IsValidating && !IsCapturing && !_enrollmentBlocked && !(IsEnrollment && SetupComplete);

    private static readonly string[] SetupDerivedProperties =
    [
        nameof(ShowSetupSteps),
        nameof(FrontSetupPhoto), nameof(LeftSetupPhoto), nameof(RightSetupPhoto),
        nameof(FrontSetupDone), nameof(LeftSetupDone), nameof(RightSetupDone),
        nameof(IsFrontStepActive), nameof(IsLeftStepActive), nameof(IsRightStepActive),
        nameof(SetupComplete), nameof(CurrentSetupPose), nameof(InstructionText),
        nameof(CanUseCameraButton), nameof(PrimaryButtonLabel)
    ];

    private void RaiseSetupChanged()
    {
        foreach (var name in SetupDerivedProperties)
            OnPropertyChanged(name);
        PrimaryActionCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
    }

    partial void OnSetupStepChanged(int value) => RaiseSetupChanged();

    private void ResetSetup()
    {
        _enrollmentSessionId = Guid.NewGuid();
        Array.Clear(_setupPhotos);
        _alreadyEnrolled = false;
        _enrollmentBlocked = false;
        _retryCommit = false;
        SetupStep = 0;
        RaiseSetupChanged();
    }

    private static int PoseIndex(string? pose) => Array.FindIndex(
        FaceSetupPoses.All, p => string.Equals(p, pose, StringComparison.OrdinalIgnoreCase));

    private int NextMissingStep()
    {
        var next = Array.FindIndex(_setupPhotos, p => p is null);
        return next < 0 ? SetupPhotoCount : next;
    }

    // ── Camera ───────────────────────────────────────────────────────────────────────────────

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
        !ShowCapturedSuccess &&
        (IsCapturing ||
         IsCaptured ||
         IsVerificationFailed ||
         !string.Equals(CaptureStatusText, DefaultPrompt, StringComparison.Ordinal));

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonLabel))]
    private string _continueLabel = "Enroll & Continue";
    [ObservableProperty] private string _employeeName = "—";
    [ObservableProperty] private string _employeeId = "—";

    public void SetContext(string? context)
    {
        _captureContext   = context;
        _capturedBytes    = null;
        CapturedPhotoBytes = null;
        IsCaptured        = false;
        IsVerificationFailed = false;
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

        ResetSetup();
        OnPropertyChanged(nameof(ShowMatchCheck));
        OnPropertyChanged(nameof(NeedsFaceSetup));
        OnPropertyChanged(nameof(PrimaryButtonLabel));
        PrimaryActionCommand.NotifyCanExecuteChanged();
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
        if (_enrollmentBlocked || (IsEnrollment && SetupComplete))
            return;

        IsCapturing       = true;
        IsScanAnimating   = true;
        IsVerificationFailed = false;
        _retryCommit      = false;
        CaptureStatusText = "Scanning your face...";
        try
        {
            var bytes = await _camera.CapturePhotoAsync(ct);
            _capturedBytes    = bytes is { Length: > 0 } ? bytes : null;
            CapturedPhotoBytes = _capturedBytes;
            IsCaptured        = _capturedBytes is not null;
            ResetValidation();
            IsVerificationFailed = !IsCaptured;
            CaptureStatusText = IsCaptured
                ? "Face captured successfully."
                : "No photo taken. Please try again.";
        }
        catch
        {
            IsCaptured        = false;
            IsVerificationFailed = true;
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

    private bool CanPrimaryAction => !IsValidating && !IsCapturing && !NeedsFaceSetup && !_enrollmentBlocked;

    /// <summary>
    /// The big button walks the employee through: Capture → (AWS check) → Verify/Enroll,
    /// or on failure "Try again" → back to the live camera → Capture again. Face setup repeats
    /// Capture for each of its three photos before "Enroll &amp; Continue".
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
    private async Task PrimaryAction(CancellationToken ct)
    {
        if (NeedsFaceSetup || _enrollmentBlocked)
            return;

        if (_retryCommit)
        {
            await Continue();
            return;
        }

        if (IsVerificationFailed)
        {
            ReturnToLivePreview();
            return;
        }

        if (IsEnrollment)
        {
            if (SetupComplete)
                await Continue();
            else
                await CapturePhotoAsync(ct);
            return;
        }

        if (!IsCaptured)
        {
            await CapturePhotoAsync(ct);
            return;
        }

        await Continue();
    }

    /// <summary>Where "Back" returns to: the screen that opened this capture.</summary>
    public string BackRoute =>
        string.Equals(_captureContext, "clockin", StringComparison.OrdinalIgnoreCase) ? SetupFlow.ClockIn
        : string.Equals(_captureContext, "clockout", StringComparison.OrdinalIgnoreCase) ? SetupFlow.Active
        : SetupFlow.ConfirmDetails;

    // Leaving mid-scan or mid-AWS-check would drop a request the employee is waiting on.
    private bool CanGoBack => !IsCapturing && !IsValidating;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(BackRoute); }
        catch { /* unit tests */ }
    }

    /// <summary>Drops the rejected photo so the employee can line up again on the live feed.</summary>
    private void ReturnToLivePreview(string? status = null)
    {
        _capturedBytes = null;
        CapturedPhotoBytes = null;
        // A stale pre-capture frame would flash before the reader delivers a fresh one.
        LivePreviewBytes = null;
        IsCaptured = false;
        IsVerificationFailed = false;
        ResetValidation();
        CaptureStatusText = status ?? DefaultPrompt;
    }

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
                IsVerificationFailed = true;
                return;
            }

            await SubmitFacePhotoRecordAsync(_capturedBytes);
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
                IsVerificationFailed = true;
                _capturedBytes = null;
                // Back to the capture screen so the failure message is visible — navigating
                // without the "context" query param avoids re-triggering SetContext, which
                // would otherwise wipe the message we just set.
                try { await Shell.Current.GoToAsync(".."); } catch { /* unit tests */ }
                return;
            }

            await SubmitFacePhotoRecordAsync(_capturedBytes);

            try { await Shell.Current.GoToAsync("//active"); }
            catch { /* unit tests */ }
            return;
        }

        await CompleteFaceSetupAsync();
    }

    /// <summary>Face setup: save the three photos (unless already enrolled), then move on.</summary>
    private async Task CompleteFaceSetupAsync()
    {
        if (!SetupComplete)
            return;

        if (!_alreadyEnrolled && !await CommitSetupPhotosAsync())
            return;

        await SubmitFacePhotoRecordAsync(_setupPhotos[0] ?? _capturedBytes);

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

    private async Task<bool> CommitSetupPhotosAsync()
    {
        IsValidating = true;
        _retryCommit = false;
        IsVerificationFailed = false;
        CaptureStatusText = "Saving your face setup...";
        FaceEnrollCommitResultPayload? result;
        try
        {
            result = await _pipe.CommitFaceEnrollmentAsync(_enrollmentSessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face setup commit failed");
            result = null;
        }
        finally
        {
            IsValidating = false;
        }

        if (result is { Success: true, Enrolled: true })
            return true;

        if (result is { Success: false, ErrorCode: "PHOTOS_MISSING" })
        {
            // Service restarted or the attempt expired — its copies are gone.
            ResetSetup();
            ReturnToLivePreview("Your photos expired. Please take the 3 photos again.");
            return false;
        }

        if (result is not { Success: true })
        {
            _retryCommit = true;
            IsVerificationFailed = true;
            CaptureStatusText = "Face setup could not be saved right now. Try again.";
            RaiseSetupChanged();
            return false;
        }

        if (result.FailureReason == FaceCheckFailureCodes.AlreadyEnrolled)
        {
            _enrollmentBlocked = true;
            IsVerificationFailed = true;
            CaptureStatusText = "A face is already set up for this employee. Contact HR to change it.";
            RaiseSetupChanged();
            return false;
        }

        // One photo failed the final check: retake just that one.
        var failedIndex = PoseIndex(result.FailedPhoto);
        if (failedIndex >= 0)
        {
            _setupPhotos[failedIndex] = null;
            SetupStep = failedIndex;
        }

        IsVerificationFailed = true;
        CaptureStatusText = BuildRetakeMessage(
            new FacePhotoValidateResultPayload(true, null, false, false, false, false, false, null, result.FailureReason),
            pose: result.FailedPhoto);
        RaiseSetupChanged();
        return false;
    }

    private void ResetValidation() => SetValidation(null);

    /// <summary>
    /// Clock-in/out: the AWS result of the captured photo gates the lifecycle call.
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
            IsVerificationFailed = true;
            CaptureStatusText = "No photo taken. Please try again.";
            return false;
        }

        var enrollment = IsEnrollment;
        var pose = enrollment ? CurrentSetupPose : null;
        IsValidating = true;
        CaptureStatusText = "Checking lighting and face...";
        try
        {
            var result = await _pipe.ValidateFacePhotoAsync(
                "jpeg", _capturedBytes, ValidatePurpose, CancellationToken.None,
                pose, enrollment ? _enrollmentSessionId : null);
            SetValidation(result);
            if (result is { Success: true, CanProceed: true })
            {
                IsVerificationFailed = false;
                if (enrollment)
                    AcceptSetupPhoto(result);
                else
                    CaptureStatusText = "Face captured successfully.";
                return true;
            }

            IsVerificationFailed = true;
            if (enrollment && result is { Success: true, FailureReason: FaceCheckFailureCodes.NotMatched })
            {
                // Face setup with a reference already on file that is someone else's face.
                _enrollmentBlocked = true;
                CaptureStatusText = "A different face is already set up for this employee. Contact HR.";
                RaiseSetupChanged();
                return false;
            }

            CaptureStatusText = BuildRetakeMessage(result, _photoLightingOk, pose);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face photo validate failed");
            SetValidation(null);
            IsVerificationFailed = true;
            CaptureStatusText = "Face verification is unavailable. Retake and try again.";
            return false;
        }
        finally
        {
            IsValidating = false;
        }
    }

    /// <summary>
    /// Face setup step passed: keep the photo and line up the next missing step on the live
    /// camera, or stay on the last photo once all three are in.
    /// </summary>
    private void AcceptSetupPhoto(FacePhotoValidateResultPayload result)
    {
        if (result.FailureReason == FaceCheckFailureCodes.AlreadyEnrolled)
        {
            _setupPhotos[0] = _capturedBytes;
            _alreadyEnrolled = true;
            SetupStep = SetupPhotoCount;
            CaptureStatusText = "Your face is already set up. Tap Enroll & Continue.";
            RaiseSetupChanged();
            return;
        }

        var step = Math.Clamp(SetupStep, 0, SetupPhotoCount - 1);
        _setupPhotos[step] = _capturedBytes;
        var next = NextMissingStep();
        SetupStep = next;

        if (next >= SetupPhotoCount)
        {
            CaptureStatusText = "All 3 photos captured. Tap Enroll & Continue.";
            RaiseSetupChanged();
            return;
        }

        var saved = step switch { 0 => "Front photo saved.", 1 => "Left photo saved.", _ => "Right photo saved." };
        var nextInstruction = next switch
        {
            0 => "Now look straight at the camera.",
            1 => "Now turn your head slightly to the left.",
            _ => "Now turn your head slightly to the right."
        };
        ReturnToLivePreview($"{saved} {nextInstruction}");
        RaiseSetupChanged();
    }

    /// <param name="photoLightingOk">Whole-photo brightness check, used when the face itself is the problem.</param>
    /// <param name="pose">Face setup step the photo was taken for (null outside face setup).</param>
    internal static string BuildRetakeMessage(
        FacePhotoValidateResultPayload? result, bool? photoLightingOk = null, string? pose = null)
    {
        if (result is null || result.Success == false)
            return "Face verification is unavailable. Retake and try again.";

        var darkHint = photoLightingOk == false ? " Also move to a brighter spot." : "";
        var which = pose is null ? "" : $"{char.ToUpperInvariant(pose[0])}{pose[1..]} photo: ";

        // Specific codes first: FaceVisible is also false for no-face and multi-face,
        // so the generic check below would otherwise swallow them.
        switch (result.FailureReason)
        {
            case FaceCheckFailureCodes.NoFaceDetected:
                return which + "No face detected. Look at the camera and retake." + darkHint;
            case FaceCheckFailureCodes.MultipleFaces:
                return which + "Only one person should be in the frame. Retake." + darkHint;
            case FaceCheckFailureCodes.FaceNotVisible:
                return which + "Your face is not fully in the frame. Centre your face in the circle and retake." + darkHint;
            case FaceCheckFailureCodes.GlassesGlare:
                return which + "Light is reflecting on your glasses and hiding your eyes. Tilt your head slightly or reduce the glare, then retake.";
            case FaceCheckFailureCodes.EyesClosed:
                return which + "Keep your eyes open and look at the camera, then retake.";
            case FaceCheckFailureCodes.WrongPose:
                return string.Equals(pose, FaceSetupPoses.Front, StringComparison.OrdinalIgnoreCase) || pose is null
                    ? which + "Look straight at the camera and retake."
                    : which + $"Turn your head a little to the {pose} and retake.";
            case FaceCheckFailureCodes.SameSide:
                return which + "Both side photos were turned the same way. Turn your head to the other side and retake.";
            case FaceCheckFailureCodes.NoReferencePhoto:
                return "No face reference on file. Contact HR to complete face setup.";
            case FaceCheckFailureCodes.VerificationFailed:
                return "Face check reached AWS but could not finish. Try again.";
            case FaceCheckFailureCodes.NotMatched:
                return pose is null
                    ? "Face did not match the enrolled employee. Retake."
                    : which + "This does not look like the same person as the front photo. Retake.";
        }

        if (!result.FaceVisible)
            return which + "Your face is not fully in the frame. Centre your face in the circle and retake." + darkHint;
        if (!result.LightingOk)
            return which + "Lighting is not good. Move to a better-lit spot and retake.";
        if (!result.NoSunglassesOrMask)
            return which + "Remove sunglasses or mask and retake.";
        if (!result.IsMatch && pose is null)
            return "Face did not match the enrolled employee. Retake.";
        return "Retake photo and try again.";
    }

    private async Task SubmitFacePhotoRecordAsync(byte[]? photo)
    {
        if (photo is not { Length: > 0 }) return;

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
                Data            = Convert.ToBase64String(photo),
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
