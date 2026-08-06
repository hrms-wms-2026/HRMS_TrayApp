namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    [ObservableProperty] private string _fullName     = string.Empty;
    [ObservableProperty] private string _workEmail    = string.Empty;
    [ObservableProperty] private string _employeeId   = string.Empty;
    [ObservableProperty] private string _workLocation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaceVerificationStatusText))]
    private bool _faceVerificationCompleted;

    public string FaceVerificationStatusText =>
        FaceVerificationCompleted ? "Completed" : "Pending";

    public ReviewSetupViewModel() { Title = "Confirm Your Details"; }

    public void OnAppearing()
    {
        FullName                  = Preferences.Get("onevo.employee_display_name", string.Empty);
        WorkEmail                 = Preferences.Get("onevo.employee_email",         string.Empty);
        EmployeeId                = Preferences.Get("onevo.employee_id",            string.Empty);
        WorkLocation              = Preferences.Get("onevo.work_location_display",  string.Empty);
        FaceVerificationCompleted = Preferences.Get("onevo.face_verified",          false);
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync("//prepare"); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task ConfirmAndContinue()
    {
        try { await Shell.Current.GoToAsync("//policy"); }
        catch { /* unit tests */ }
    }
}
