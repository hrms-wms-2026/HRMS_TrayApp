namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _profileLoaded;
    [ObservableProperty] private bool _permissionsChecked;
    [ObservableProperty] private bool _companySettingsVerified;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName   = string.Empty;
    [ObservableProperty] private string _employeeEmail      = string.Empty;
    [ObservableProperty] private string _employeeDepartment = string.Empty;
    [ObservableProperty] private string _selectedWorkLocation = string.Empty;

    public bool CanContinue =>
        ActivationVerified && ProfileLoaded && PermissionsChecked && CompanySettingsVerified;

    public PrepareWorkspaceViewModel()
    {
        Title = "Preparing Your Workspace";
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(500, ct);
        ActivationVerified = true;

        await Task.Delay(800, ct);
        ProfileLoaded      = true;
        EmployeeFullName   = "Loading…";
        EmployeeEmail      = "Loading…";
        EmployeeDepartment = "Loading…";

        await Task.Delay(600, ct);
        PermissionsChecked = true;

        await Task.Delay(400, ct);
        CompanySettingsVerified = true;

        IsLoading = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private static void ContinueSetup()
    {
        // Navigation wired in AppShell
    }
}
