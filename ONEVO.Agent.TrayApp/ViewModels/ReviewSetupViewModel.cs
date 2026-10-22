namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;
    private readonly INamedPipeClient? _pipe;

    [ObservableProperty] private string _fullName     = string.Empty;
    [ObservableProperty] private string _workEmail    = string.Empty;
    [ObservableProperty] private string _employeeId   = string.Empty;
    [ObservableProperty] private string _department   = "—";
    [ObservableProperty] private string _registeredOffice = "—";
    [ObservableProperty] private string _workMode     = "—";
    [ObservableProperty] private string _thisDevice   = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaceVerificationStatusText))]
    private bool _faceVerificationCompleted;

    public string FaceVerificationStatusText =>
        FaceVerificationCompleted ? "Enrolled" : "Pending";

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public ReviewSetupViewModel(
        IPreferencesStore preferences, ServiceConnectionStatus? connection = null, INamedPipeClient? pipe = null)
    {
        Connection = connection ?? new ServiceConnectionStatus();
        Title = "Confirm Your Details";
        _preferences = preferences;
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        FullName                  = SetupFlow.DisplayOrDash(EmployeeSession.Name(_preferences));
        WorkEmail                 = SetupFlow.DisplayOrDash(EmployeeSession.Email(_preferences));
        EmployeeId                = SetupFlow.DisplayOrDash(EmployeeSession.Id(_preferences));
        Department                = SetupFlow.DisplayOrDash(EmployeeSession.Department(_preferences));
        RegisteredOffice          = SetupFlow.DisplayOrDash(EmployeeSession.Office(_preferences));
        WorkMode                  = SetupFlow.DisplayOrDash(EmployeeSession.WorkMode(_preferences));
        ThisDevice                = SetupFlow.DisplayOrDash(EmployeeSession.DeviceName(_preferences));
        FaceVerificationCompleted = string.Equals(
            _preferences.Get(SessionPreferenceKeys.FaceVerified, string.Empty),
            "true",
            StringComparison.OrdinalIgnoreCase)
            || _preferences.Get(SessionPreferenceKeys.FaceVerified, "false") == "True";
        try
        {
            FaceVerificationCompleted = Microsoft.Maui.Storage.Preferences.Get("onevo.face_verified", FaceVerificationCompleted);
        }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.Connect); }
        catch { /* unit tests */ }
    }

    /// <summary>
    /// Skips what the backend already holds for this employee: face setup when a face is enrolled
    /// (clock-in still verifies against it every time), and the Privacy and Permissions screens
    /// when their acceptance still covers the current policy. When an answer is unknown
    /// (Service/backend down, or an older Service), that step is shown as before.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAndContinue()
    {
        var route = SetupFlow.AfterConfirmDetails;
        if (_pipe is not null)
        {
            var faceTask = SafeAsync(() => _pipe.GetFaceReferenceStatusAsync(CancellationToken.None));
            var consentTask = SafeAsync(() => _pipe.GetPermissionConsentAsync(CancellationToken.None));
            var face = await faceTask;
            var consent = await consentTask;

            var consentOnFile = SetupFlow.ConsentCovers(consent, _pipe.LastKnownPolicy);
            _preferences.Set(SessionPreferenceKeys.PermissionConsentOnFile, consentOnFile ? "true" : "false");

            if (face is { Success: true, Enrolled: true })
            {
                _preferences.Set(SessionPreferenceKeys.FaceVerified, "true");
                try { Microsoft.Maui.Storage.Preferences.Set("onevo.face_verified", true); }
                catch { /* unit tests */ }
                FaceVerificationCompleted = true;
                route = SetupFlow.AfterFaceEnrollment(
                    _pipe.LastKnownPolicy?.AllowsDailyLocationChoice ?? false, consentOnFile);
            }
        }

        LastRoute = route;
        try { await Shell.Current.GoToAsync(route); }
        catch { /* unit tests */ }
    }

    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> call) where T : class
    {
        try { return await call(); }
        catch { return null; /* unknown — show the step */ }
    }

    /// <summary>Where Confirm &amp; Continue went last (for tests; Shell is absent there).</summary>
    public string? LastRoute { get; private set; }
}
