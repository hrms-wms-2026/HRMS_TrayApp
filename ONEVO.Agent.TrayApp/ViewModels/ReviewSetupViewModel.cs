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

    [RelayCommand]
    private static void Back()
    {
        // Navigate back to PrepareWorkspacePage
    }

    [RelayCommand]
    private static void ConfirmAndContinue()
    {
        // Navigate to PrivacyConsentPage
    }
}
