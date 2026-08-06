namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.Shared.Models;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    // Required by policy — always true, toggle locked
    [ObservableProperty] private bool _activitySignalEnabled = true;
    public bool ActivitySignalRequired => true;

    [ObservableProperty] private bool _applicationUsageEnabled = true;
    [ObservableProperty] private bool _workLocationEnabled     = true;
    [ObservableProperty] private bool _cameraAccessEnabled     = false;
    [ObservableProperty] private bool _notificationsEnabled    = true;
    [ObservableProperty] private bool _keyboardMouseEnabled    = true;

    [ObservableProperty] private bool _policyAcknowledged;

    public PrivacyConsentViewModel() { Title = "Privacy, Monitoring and Required Permissions"; }

    public void ApplyPolicy(AgentPolicy policy)
    {
        ApplicationUsageEnabled = policy.AppUsageEnabled;
        CameraAccessEnabled     = policy.CameraVerificationEnabled;
    }

    [RelayCommand(CanExecute = nameof(PolicyAcknowledged))]
    private static void ReviewAndContinue()
    {
        // Navigate to ClockInPage
    }
}
