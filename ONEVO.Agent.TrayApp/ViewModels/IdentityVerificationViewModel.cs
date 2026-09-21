namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Clock-in dwell screen shown only after AWS DetectFaces + CompareFaces already passed.
/// Checklist and match % come from <see cref="CapturedPhotoBuffer.LastValidation"/>.
/// </summary>
public sealed partial class IdentityVerificationViewModel : BaseViewModel
{
    private readonly CapturedPhotoBuffer _photoBuffer;
    private readonly IPreferencesStore? _preferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchPercentageText))]
    [NotifyPropertyChangedFor(nameof(MatchProgress))]
    private double _matchPercentage;

    [ObservableProperty] private string _statusText = "Matching identity...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerificationPhoto))]
    private ImageSource? _verificationPhotoSource;

    [ObservableProperty] private bool _isGoodLighting;
    [ObservableProperty] private bool _isFaceVisible;
    [ObservableProperty] private bool _isNoMaskOrGlasses;
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

        var validation = _photoBuffer.LastValidation;
        IsGoodLighting = validation?.LightingOk == true;
        IsFaceVisible = validation?.FaceVisible == true;
        IsNoMaskOrGlasses = validation?.NoSunglassesOrMask == true;
        MatchPercentage = validation?.Similarity ?? 0;
        StatusText = validation?.CanProceed == true ? "Identity matched" : "Matching identity...";
    }
}
