namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _preferences;

    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled    = true;
    [ObservableProperty] private bool _locationAccessEnabled = true;
    [ObservableProperty] private bool _cameraAccessEnabled   = false;
    [ObservableProperty] private bool _notificationsEnabled  = true;
    [ObservableProperty] private bool _keyboardMouseEnabled  = true;

    public PrivacyConsentViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title = "Allow Required Policies";
        _pipe = pipe;
        _preferences = preferences;
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
    private async Task AllowAndContinue()
    {
        WorkLocationFlow.MarkSetupComplete(_preferences);
        try { await Shell.Current.GoToAsync(WorkLocationFlow.RouteToStartWork(_preferences)); }
        catch { /* unit tests */ }
    }
}
