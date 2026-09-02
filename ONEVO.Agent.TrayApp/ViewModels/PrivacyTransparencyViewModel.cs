namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyTransparencyViewModel : BaseViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _hasAgreed;

    public PrivacyTransparencyViewModel()
    {
        Title = "Privacy & Transparency";
    }

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
