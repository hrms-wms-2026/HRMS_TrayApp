namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private bool _screenMonitoringEnabled = true;
    [ObservableProperty] private bool _appTrackingEnabled      = true;
    [ObservableProperty] private bool _cameraAccessEnabled     = false;
    [ObservableProperty] private bool _notificationsEnabled    = true;
    [ObservableProperty] private bool _keyboardMouseEnabled    = true;

    public PrivacyConsentViewModel(INamedPipeClient pipe)
    {
        Title = "Allow Required Policies";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    /// <summary>
    /// All switches on this screen are display-only — they mirror the tenant-configured
    /// AgentPolicy, they are never a per-employee opt-out (see the footer copy in
    /// PrivacyConsentPage.xaml). Notifications has no AgentPolicy field because it isn't a
    /// monitoring capability, so it stays at its default.
    /// </summary>
    public void ApplyPolicy(AgentPolicy policy)
    {
        ScreenMonitoringEnabled = policy.ScreenshotEnabled;
        AppTrackingEnabled      = policy.AppUsageEnabled;
        CameraAccessEnabled     = policy.CameraVerificationEnabled;
        KeyboardMouseEnabled    = policy.ActivitySignalEnabled;
    }

    [RelayCommand]
    private async Task AllowAndContinue()
    {
        try { await Shell.Current.GoToAsync("//clockin"); }
        catch { /* unit tests */ }
    }
}
