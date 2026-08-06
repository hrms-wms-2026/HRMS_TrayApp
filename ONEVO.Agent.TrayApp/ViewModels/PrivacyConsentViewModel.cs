namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled    = true;
    [ObservableProperty] private bool _locationAccessEnabled = true;
    [ObservableProperty] private bool _cameraAccessEnabled   = false;
    [ObservableProperty] private bool _notificationsEnabled  = true;
    [ObservableProperty] private bool _keyboardMouseEnabled  = true;

    public PrivacyConsentViewModel() { Title = "Allow Required Policies"; }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled;
    }

    [RelayCommand]
    private static void AllowAndContinue()
    {
        // Navigate to ClockInPage
    }
}
