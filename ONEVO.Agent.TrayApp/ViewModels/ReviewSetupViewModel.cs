namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    [ObservableProperty] private string _fullName          = string.Empty;
    [ObservableProperty] private string _workEmail         = string.Empty;
    [ObservableProperty] private string _department        = string.Empty;
    [ObservableProperty] private string _manager           = string.Empty;
    [ObservableProperty] private string _workLocation      = string.Empty;
    [ObservableProperty] private string _monitoringManager = string.Empty;
    [ObservableProperty] private string _registeredDevice  = string.Empty;
    [ObservableProperty] private DateTimeOffset _lastUpdated = DateTimeOffset.UtcNow;
    [ObservableProperty] private bool _hasSetupErrors;

    public ReviewSetupViewModel() { Title = "Review Your Setup"; }

    [RelayCommand]
    private static void EditSetup()
    {
        // Navigate back to PrepareWorkspacePage
    }

    [RelayCommand]
    private static void ConfirmSetup()
    {
        // Navigate to PrivacyConsentPage
    }
}
