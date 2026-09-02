namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// View model for the clock-in "Verify Your Identity" dwell screen. Shows the just-captured
/// selfie with sample match presentation state; the real face-match result wiring is a
/// separate task — this page does not decide clock-in success or failure.
/// </summary>
public sealed partial class IdentityVerificationViewModel : BaseViewModel
{
    private readonly CapturedPhotoBuffer _photoBuffer;
    private readonly IPreferencesStore? _preferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchPercentageText))]
    [NotifyPropertyChangedFor(nameof(MatchProgress))]
    private double _matchPercentage = 82;

    [ObservableProperty] private string _statusText = "Matching identity...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerificationPhoto))]
    private ImageSource? _verificationPhotoSource;

    [ObservableProperty] private bool _isGoodLighting = true;
    [ObservableProperty] private bool _isFaceVisible = true;
    [ObservableProperty] private bool _isNoMaskOrGlasses = true;
    [ObservableProperty] private string _employeeName = "—";
    [ObservableProperty] private string _employeeId = "—";

    public string MatchPercentageText => $"{MatchPercentage:0}%";
    public double MatchProgress => MatchPercentage / 100;
    public bool HasVerificationPhoto => VerificationPhotoSource is not null;

    public IdentityVerificationViewModel(CapturedPhotoBuffer photoBuffer, IPreferencesStore? preferences = null)
    {
        Title = "Verify Your Identity";
        _photoBuffer = photoBuffer;
        _preferences = preferences;
    }

    /// <summary>Call from the page's OnAppearing so the freshly-captured selfie shows in the ring.</summary>
    public void LoadCapturedPhoto()
    {
        if (_preferences is not null)
        {
            EmployeeName = SetupFlow.DisplayOrDash(EmployeeSession.Name(_preferences));
            EmployeeId = SetupFlow.DisplayOrDash(EmployeeSession.Id(_preferences));
        }
        var bytes = _photoBuffer.Bytes;
        VerificationPhotoSource = bytes is { Length: > 0 }
            ? ImageSource.FromStream(() => new MemoryStream(bytes))
            : null;
    }
}
