namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    public bool CanContinue => ActivationVerified && UserDetailsFetched && WorkspacePrepared;

    public PrepareWorkspaceViewModel() { Title = "Setting Up Your Workspace"; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(600, ct);
        ActivationVerified = true;

        await Task.Delay(900, ct);
        UserDetailsFetched = true;
        EmployeeFullName   = "Pirakeerthan";
        EmployeeEmail      = "pirakeerthan@onevo.com";
        EmployeeId         = "ONEVO1234";
        Preferences.Set("onevo.employee_display_name", EmployeeFullName);
        Preferences.Set("onevo.employee_email",        EmployeeEmail);
        Preferences.Set("onevo.employee_id",           EmployeeId);
        OnPropertyChanged(nameof(CanContinue));

        await Task.Delay(500, ct);
        WorkspacePrepared = true;
        IsLoading         = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueSetup()
    {
        try { await Shell.Current.GoToAsync("//location"); }
        catch { /* unit tests */ }
    }
}
