namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;
    private readonly IWorkLocationStore _workLocationStore;

    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _deviceRegistered;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    [ObservableProperty] private bool _isLocationConfirmed;
    [ObservableProperty] private string _locationStatusText = "Not confirmed yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingUp))]
    [NotifyPropertyChangedFor(nameof(ShowWelcomeBack))]
    private string _stage = "setting";

    [ObservableProperty] private int _progressPercent;

    [ObservableProperty] private bool _welcomeServerDone;
    [ObservableProperty] private bool _welcomeInternetDone;
    [ObservableProperty] private bool _welcomeProfileDone;
    [ObservableProperty] private bool _welcomePoliciesDone;
    [ObservableProperty] private bool _welcomeWorkspaceDone;

    public bool ShowSettingUp => Stage == "setting";
    public bool ShowWelcomeBack => Stage == "welcome";

    public bool WelcomePoliciesInProgress => ShowWelcomeBack && WelcomeProfileDone && !WelcomePoliciesDone;
    public bool WelcomeWorkspaceInProgress => ShowWelcomeBack && WelcomePoliciesDone && !WelcomeWorkspaceDone;
    public string WelcomeServerDuration => WelcomeServerDone ? "2.1s" : "--";
    public string WelcomeInternetDuration => WelcomeInternetDone ? "1.3s" : "--";
    public string WelcomeProfileDuration => WelcomeProfileDone ? "1.8s" : "--";
    public string WelcomePoliciesDuration => WelcomePoliciesDone ? "1.2s" : WelcomePoliciesInProgress ? "..." : "--";
    public string WelcomeWorkspaceDuration => WelcomeWorkspaceDone ? "0.8s" : WelcomeWorkspaceInProgress ? "..." : "--";
    public string WelcomePoliciesStatus =>
        WelcomePoliciesDone ? "Completed" : WelcomePoliciesInProgress ? "In progress" : string.Empty;
    public string WelcomeWorkspaceStatus =>
        WelcomeWorkspaceDone ? "Completed" : WelcomeWorkspaceInProgress ? "In progress" : string.Empty;

    public int SettingProgressPercent
    {
        get
        {
            var completed =
                (ActivationVerified ? 1 : 0)
                + (UserDetailsFetched ? 1 : 0)
                + (DeviceRegistered ? 1 : 0)
                + (WorkspacePrepared ? 1 : 0);
            if (completed == 0 && IsLoading)
                return 12;
            return completed * 25;
        }
    }

    public bool ShowSettingCheck => DeviceRegistered;

    public bool ActivationInProgress => ShowSettingUp && !ActivationVerified;
    public bool DetailsInProgress => ShowSettingUp && ActivationVerified && !UserDetailsFetched;
    public bool DeviceInProgress => ShowSettingUp && UserDetailsFetched && !DeviceRegistered;
    public bool WorkspaceInProgress => ShowSettingUp && DeviceRegistered && !WorkspacePrepared;

    public string ActivationStepStatus => StepStatus(ActivationVerified, ActivationInProgress);
    public string DetailsStepStatus => StepStatus(UserDetailsFetched, DetailsInProgress);
    public string DeviceStepStatus => StepStatus(DeviceRegistered, DeviceInProgress);
    public string WorkspaceStepStatus => StepStatus(WorkspacePrepared, WorkspaceInProgress);

    private static string StepStatus(bool done, bool inProgress) =>
        done ? "Completed" : inProgress ? "In progress" : string.Empty;

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

    public void RefreshLocationStatus()
    {
        var reference = _workLocationStore.Load();
        IsLocationConfirmed = reference is not null;
        LocationStatusText = reference is not null
            ? $"Confirmed: {reference.DisplayName}"
            : "Not confirmed yet";
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            RefreshLocationStatus();
            return;
        }

        IsLoading = true;
        LoadEmployee();

        if (WorkLocationFlow.IsSetupComplete(_preferences))
        {
            Stage = "welcome";
            ActivationVerified = UserDetailsFetched = DeviceRegistered = WorkspacePrepared = true;
            WelcomeServerDone = true;
            ProgressPercent = 40;
            IsLoading = false;
            _loaded = true;
            await Task.Delay(280, ct);
            WelcomeInternetDone = true;
            ProgressPercent = 55;
            await Task.Delay(280, ct);
            WelcomeProfileDone = true;
            ProgressPercent = 68;
            await Task.Delay(280, ct);
            WelcomePoliciesDone = true;
            ProgressPercent = 85;
            await Task.Delay(280, ct);
            WelcomeWorkspaceDone = true;
            ProgressPercent = 100;
            try { await Shell.Current.GoToAsync(SetupFlow.ClockIn); }
            catch { /* unit tests */ }
            return;
        }

        Stage = "setting";
        await Task.Delay(500, ct);
        ActivationVerified = true;

        await Task.Delay(500, ct);
        UserDetailsFetched = true;
        LoadEmployee();

        await Task.Delay(400, ct);
        DeviceRegistered = true;

        await Task.Delay(400, ct);
        WorkspacePrepared = true;

        WorkLocationFlow.MarkSetupComplete(_preferences);
        ProgressPercent = 100;
        IsLoading = false;
        _loaded = true;
        try { await Shell.Current.GoToAsync(SetupFlow.AfterWorkspaceReady); }
        catch { /* unit tests */ }
    }

    partial void OnStageChanged(string value)
    {
        RaiseSettingStepUi();
        RaiseWelcomeStepUi();
    }

    partial void OnActivationVerifiedChanged(bool value) => RaiseSettingStepUi();
    partial void OnUserDetailsFetchedChanged(bool value) => RaiseSettingStepUi();
    partial void OnDeviceRegisteredChanged(bool value) => RaiseSettingStepUi();
    partial void OnWorkspacePreparedChanged(bool value) => RaiseSettingStepUi();
    partial void OnIsLoadingChanged(bool value) => RaiseSettingStepUi();
    partial void OnWelcomeServerDoneChanged(bool value) => RaiseWelcomeStepUi();
    partial void OnWelcomeInternetDoneChanged(bool value) => RaiseWelcomeStepUi();
    partial void OnWelcomeProfileDoneChanged(bool value) => RaiseWelcomeStepUi();
    partial void OnWelcomePoliciesDoneChanged(bool value) => RaiseWelcomeStepUi();
    partial void OnWelcomeWorkspaceDoneChanged(bool value) => RaiseWelcomeStepUi();

    private void RaiseSettingStepUi()
    {
        OnPropertyChanged(nameof(SettingProgressPercent));
        OnPropertyChanged(nameof(ShowSettingCheck));
        OnPropertyChanged(nameof(ActivationInProgress));
        OnPropertyChanged(nameof(DetailsInProgress));
        OnPropertyChanged(nameof(DeviceInProgress));
        OnPropertyChanged(nameof(WorkspaceInProgress));
        OnPropertyChanged(nameof(ActivationStepStatus));
        OnPropertyChanged(nameof(DetailsStepStatus));
        OnPropertyChanged(nameof(DeviceStepStatus));
        OnPropertyChanged(nameof(WorkspaceStepStatus));
    }

    private void RaiseWelcomeStepUi()
    {
        OnPropertyChanged(nameof(WelcomePoliciesInProgress));
        OnPropertyChanged(nameof(WelcomeWorkspaceInProgress));
        OnPropertyChanged(nameof(WelcomeServerDuration));
        OnPropertyChanged(nameof(WelcomeInternetDuration));
        OnPropertyChanged(nameof(WelcomeProfileDuration));
        OnPropertyChanged(nameof(WelcomePoliciesDuration));
        OnPropertyChanged(nameof(WelcomeWorkspaceDuration));
        OnPropertyChanged(nameof(WelcomePoliciesStatus));
        OnPropertyChanged(nameof(WelcomeWorkspaceStatus));
    }

    private void LoadEmployee()
    {
        EmployeeFullName = EmployeeSession.Name(_preferences);
        EmployeeEmail    = EmployeeSession.Email(_preferences);
        EmployeeId       = EmployeeSession.Id(_preferences);
    }
}
