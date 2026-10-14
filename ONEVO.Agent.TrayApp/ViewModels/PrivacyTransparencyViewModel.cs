namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyTransparencyViewModel : BaseViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _hasAgreed;

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public PrivacyTransparencyViewModel(ServiceConnectionStatus? connection = null)
    {
        Connection = connection ?? new ServiceConnectionStatus();
        Title = "Privacy & Transparency";
    }

    [RelayCommand]
    private void ToggleAgree() => HasAgreed = !HasAgreed;

    [RelayCommand]
    private static void OpenPrivacyPolicy()
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

    private bool CanContinue => HasAgreed;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.AfterPrivacy); }
        catch { /* unit tests */ }
    }
}
