namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;
    private readonly IWorkLocationStore _workLocationStore;

    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    [ObservableProperty] private bool _isLocationConfirmed;
    [ObservableProperty] private string _locationStatusText = "Not confirmed yet";

    public bool CanContinue =>
        ActivationVerified && UserDetailsFetched && WorkspacePrepared && IsLocationConfirmed;

    public bool ShouldOpenLocation => !IsLocationConfirmed;

    private bool _loaded;

    public PrepareWorkspaceViewModel(IPreferencesStore preferences, IWorkLocationStore workLocationStore)
    {
        _preferences = preferences;
        _workLocationStore = workLocationStore;
        Title = "Setting Up Your Workspace";
        RefreshLocationStatus();
    }

    /// <summary>Re-reads the saved work-location reference. Call from the page's OnAppearing
    /// after returning from the location-confirmation screen so the card reflects the latest state.</summary>
    public void RefreshLocationStatus()
    {
        var reference = _workLocationStore.Load();
        IsLocationConfirmed = reference is not null;
        LocationStatusText = reference is not null
            ? $"Confirmed: {reference.DisplayName}"
            : "Not confirmed yet";
        ContinueSetupCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            RefreshLocationStatus();
            await OpenLocationIfNeeded();
            return;
        }

        IsLoading = true;

        await Task.Delay(600, ct);
        ActivationVerified = true;

        await Task.Delay(900, ct);
        UserDetailsFetched = true;
        EmployeeFullName = _preferences.Get(SessionPreferenceKeys.EmployeeDisplayName, string.Empty);
        EmployeeEmail    = _preferences.Get(SessionPreferenceKeys.EmployeeEmail, string.Empty);
        EmployeeId       = _preferences.Get(SessionPreferenceKeys.EmployeeId, string.Empty);
        OnPropertyChanged(nameof(CanContinue));
        ContinueSetupCommand.NotifyCanExecuteChanged();

        await Task.Delay(500, ct);
        WorkspacePrepared = true;
        IsLoading         = false;
        _loaded = true;
        OnPropertyChanged(nameof(CanContinue));
        ContinueSetupCommand.NotifyCanExecuteChanged();

        await OpenLocationIfNeeded();
    }

    private async Task OpenLocationIfNeeded()
    {
        if (!ShouldOpenLocation)
            return;

        try { await Shell.Current.GoToAsync(WorkLocationFlow.LocationThenPrepare); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task NavigateToPhoto()
    {
        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task NavigateToLocation()
    {
        try { await Shell.Current.GoToAsync(WorkLocationFlow.LocationThenPrepare); }
        catch { /* unit tests */ }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueSetup()
    {
        try { await Shell.Current.GoToAsync("//photo"); }
        catch { /* unit tests */ }
    }
}
