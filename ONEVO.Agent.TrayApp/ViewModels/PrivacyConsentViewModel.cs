namespace ONEVO.Agent.TrayApp.ViewModels;

using Microsoft.Extensions.Logging;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly ILogger<PrivacyConsentViewModel>? _logger;

    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled    = true;
    [ObservableProperty] private bool _locationAccessEnabled = true;
    [ObservableProperty] private bool _cameraAccessEnabled   = false;
    [ObservableProperty] private bool _notificationsEnabled  = true;
    [ObservableProperty] private bool _keyboardMouseEnabled  = true;

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public PrivacyConsentViewModel(
        INamedPipeClient pipe,
        ServiceConnectionStatus? connection = null,
        ILogger<PrivacyConsentViewModel>? logger = null)
    {
        Connection = connection ?? new ServiceConnectionStatus();
        Title = "Allow Required Permissions";
        _pipe = pipe;
        _logger = logger;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled || policy.PhotoRequiredEnabled;
    }

    partial void OnScreenMonitoringEnabledChanged(bool value)
    {
        // Required by policy — keep the toggle on.
        if (!value)
            ScreenMonitoringEnabled = true;
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

    /// <summary>
    /// Saves the acceptance on the backend so later sign-ins skip the Privacy and Permissions
    /// screens. A failed save never blocks setup — the screens simply come back next sign-in.
    /// </summary>
    [RelayCommand]
    private async Task AllowAndContinue()
    {
        try
        {
            var result = await _pipe.SubmitPermissionConsentAsync(
                new PermissionConsentPayload(
                    ScreenMonitoringEnabled, AppTrackingEnabled, LocationAccessEnabled,
                    CameraAccessEnabled, NotificationsEnabled, KeyboardMouseEnabled),
                CancellationToken.None);
            if (result is not { Success: true })
                _logger?.LogWarning("Permission consent was not saved: {Error}", result?.ErrorCode ?? "no reply");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Permission consent was not saved");
        }

        try { await Shell.Current.GoToAsync(SetupFlow.AfterPermissions); }
        catch { /* unit tests */ }
    }
}
