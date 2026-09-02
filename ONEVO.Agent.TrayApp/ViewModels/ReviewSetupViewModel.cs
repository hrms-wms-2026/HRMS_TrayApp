namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;

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

    public ReviewSetupViewModel(IPreferencesStore preferences)
    {
        Title = "Confirm Your Details";
        _preferences = preferences;
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

    [RelayCommand]
    private async Task ConfirmAndContinue()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.AfterConfirmDetails); }
        catch { /* unit tests */ }
    }
}
