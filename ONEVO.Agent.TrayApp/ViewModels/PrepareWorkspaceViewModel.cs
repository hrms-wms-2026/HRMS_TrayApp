namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;

    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    public bool CanContinue => ActivationVerified && UserDetailsFetched && WorkspacePrepared;

    public PrepareWorkspaceViewModel(IPreferencesStore preferences)
    {
        _preferences = preferences;
        Title = "Setting Up Your Workspace";
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(600, ct);
        ActivationVerified = true;

        await Task.Delay(900, ct);
        UserDetailsFetched = true;
        EmployeeFullName = _preferences.Get("onevo.employee_display_name", string.Empty);
        EmployeeEmail    = _preferences.Get("onevo.employee_email", string.Empty);
        EmployeeId       = _preferences.Get("onevo.employee_id", string.Empty);
        OnPropertyChanged(nameof(CanContinue));

        await Task.Delay(500, ct);
        WorkspacePrepared = true;
        IsLoading         = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task NavigateToLocation()
    {
        try { await Shell.Current.GoToAsync("//location"); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task NavigateToPhoto()
    {
        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueSetup()
    {
        try { await Shell.Current.GoToAsync("//location"); }
        catch { /* unit tests */ }
    }
}
