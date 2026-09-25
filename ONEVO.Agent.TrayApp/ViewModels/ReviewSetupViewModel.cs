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

    public ReviewSetupViewModel(IPreferencesStore preferences, INamedPipeClient? pipe = null)
    {
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
    /// Face setup is skipped when the backend already holds this employee's enrolled face (e.g.
    /// tray reinstalled, or a second device): clock-in still verifies against it every time.
    /// When the answer is unknown (Service/backend down), face setup is shown as before.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAndContinue()
    {
        var route = SetupFlow.AfterConfirmDetails;
        FaceReferenceStatusResultPayload? status = null;
        if (_pipe is not null)
        {
            try { status = await _pipe.GetFaceReferenceStatusAsync(CancellationToken.None); }
            catch { /* unknown — show face setup */ }
        }

        if (status is { Success: true, Enrolled: true })
        {
            _preferences.Set(SessionPreferenceKeys.FaceVerified, "true");
            try { Microsoft.Maui.Storage.Preferences.Set("onevo.face_verified", true); }
            catch { /* unit tests */ }
            FaceVerificationCompleted = true;
            route = SetupFlow.AfterFaceEnrollment(_pipe!.LastKnownPolicy?.AllowsDailyLocationChoice ?? false);
        }

        LastRoute = route;
        try { await Shell.Current.GoToAsync(route); }
        catch { /* unit tests */ }
    }

    /// <summary>Where Confirm &amp; Continue went last (for tests; Shell is absent there).</summary>
    public string? LastRoute { get; private set; }
}
