namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled    = true;
    [ObservableProperty] private bool _locationAccessEnabled = true;
    [ObservableProperty] private bool _cameraAccessEnabled   = false;
    [ObservableProperty] private bool _notificationsEnabled  = true;
    [ObservableProperty] private bool _keyboardMouseEnabled  = true;

    public PrivacyConsentViewModel(INamedPipeClient pipe)
    {
        Title = "Allow Required Permissions";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled;
    }

    [RelayCommand]
    private static void WhyNeeded()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = WorkspaceLinks.PortalUrl,
                UseShellExecute = true
            });
        }
        catch { /* browser unavailable */ }
    }

    [RelayCommand]
    private async Task AllowAndContinue()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.AfterPermissions); }
        catch { /* unit tests */ }
    }
}
