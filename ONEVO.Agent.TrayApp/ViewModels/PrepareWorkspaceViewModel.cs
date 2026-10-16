namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Diagnostics;
using Microsoft.Maui.Networking;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;
    private readonly IWorkLocationStore _workLocationStore;
    private readonly INamedPipeClient? _pipe;
    private readonly bool _connectionWired;

    /// <summary>How long the welcome-back checks wait for the Service to connect / push policy.</summary>
    internal TimeSpan ServiceWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

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

    // Welcome-back check results: real outcome text + measured time (no invented values).
    [ObservableProperty] private string _welcomeServerStatus = "Checking…";
    [ObservableProperty] private string _welcomeInternetStatus = "Checking…";
    [ObservableProperty] private string _welcomeProfileStatus = "Checking…";
    [ObservableProperty] private string _welcomePoliciesCheckStatus = "Checking…";
    [ObservableProperty] private string _welcomeServerDuration = "--";
    [ObservableProperty] private string _welcomeInternetDuration = "--";
    [ObservableProperty] private string _welcomeProfileDuration = "--";
    [ObservableProperty] private string _welcomePoliciesDuration = "--";
    [ObservableProperty] private string _welcomeWorkspaceDuration = "--";

    public bool ShowSettingUp => Stage == "setting";
    public bool ShowWelcomeBack => Stage == "welcome";

    /// <summary>Number of welcome-back checks that have finished (pass or fail).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WelcomeServerInProgress))]
    [NotifyPropertyChangedFor(nameof(WelcomePoliciesInProgress))]
    [NotifyPropertyChangedFor(nameof(WelcomeWorkspaceInProgress))]
    [NotifyPropertyChangedFor(nameof(WelcomePoliciesStatus))]
    [NotifyPropertyChangedFor(nameof(WelcomeWorkspaceStatus))]
    private int _welcomeChecksFinished;

    public bool WelcomeServerInProgress => ShowWelcomeBack && WelcomeChecksFinished == 0;
    public bool WelcomePoliciesInProgress => ShowWelcomeBack && WelcomeChecksFinished == 3;
    public bool WelcomeWorkspaceInProgress => ShowWelcomeBack && WelcomeChecksFinished == 4;
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

    /// <summary>True once the setup checks have run (pass or fail) — a failed step is then
    /// shown as "Not confirmed" instead of spinning forever.</summary>
    [ObservableProperty] private bool _settingChecksFinished;

    public bool ActivationInProgress => ShowSettingUp && !SettingChecksFinished && !ActivationVerified;
    public bool DetailsInProgress => ShowSettingUp && !SettingChecksFinished && ActivationVerified && !UserDetailsFetched;
    public bool DeviceInProgress => ShowSettingUp && !SettingChecksFinished && UserDetailsFetched && !DeviceRegistered;
    public bool WorkspaceInProgress => ShowSettingUp && !SettingChecksFinished && DeviceRegistered && !WorkspacePrepared;

    public string ActivationStepStatus => StepStatus(ActivationVerified, ActivationInProgress);
    public string DetailsStepStatus => StepStatus(UserDetailsFetched, DetailsInProgress);
    public string DeviceStepStatus => StepStatus(DeviceRegistered, DeviceInProgress);
    public string WorkspaceStepStatus => StepStatus(WorkspacePrepared, WorkspaceInProgress);

    private string StepStatus(bool done, bool inProgress) =>
        done ? "Completed" : SettingChecksFinished ? "Not confirmed" : inProgress ? "In progress" : string.Empty;

    public bool CanContinue =>
        ActivationVerified && UserDetailsFetched && WorkspacePrepared && IsLocationConfirmed;

    public bool ShouldOpenLocation => !IsLocationConfirmed;

    private bool _loaded;

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public PrepareWorkspaceViewModel(IPreferencesStore preferences, IWorkLocationStore workLocationStore,
        ServiceConnectionStatus? connection = null, INamedPipeClient? pipe = null)
    {
        Connection = connection ?? new ServiceConnectionStatus();
        _connectionWired = connection is not null;
        _pipe = pipe;
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
            IsLoading = false;
            _loaded = true;
            await RunWelcomeChecksAsync(ct);
            try { await Shell.Current.GoToAsync(SetupFlow.ClockIn); }
            catch { /* unit tests */ }
            return;
        }

        Stage = "setting";

        // Enrollment (activation + device registration) is owned by the Service: confirm it from
        // the Service's own state rather than ticking boxes on a timer.
        var enrolled = await IsServiceEnrolledAsync(ct);
        ActivationVerified = enrolled;

        LoadEmployee();
        UserDetailsFetched = !string.IsNullOrWhiteSpace(EmployeeFullName);

        DeviceRegistered = enrolled
            && !string.IsNullOrWhiteSpace(_preferences.Get(SessionPreferenceKeys.DeviceName, string.Empty));

        WorkspacePrepared = true;
        SettingChecksFinished = true;

        WorkLocationFlow.MarkSetupComplete(_preferences);
        ProgressPercent = 100;
        IsLoading = false;
        _loaded = true;
        try { await Shell.Current.GoToAsync(SetupFlow.AfterWorkspaceReady); }
        catch { /* unit tests */ }
    }

    /// <summary>
    /// Welcome-back checks against real state. A failed check is reported as such (no check mark)
    /// but does not block opening the workspace — Clock In shows live status from there.
    /// </summary>
    private async Task RunWelcomeChecksAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var serverUp = Connection.IsConnected
            || (_connectionWired && await WaitForAsync(() => Connection.IsConnected, ct));
        WelcomeServerStatus = serverUp ? "Secure connection established" : "WorkPulse service not reachable";
        WelcomeServerDuration = Elapsed(sw);
        WelcomeServerDone = serverUp;
        WelcomeChecksFinished = 1;
        ProgressPercent = 20;

        sw.Restart();
        bool? online;
        try { online = Connectivity.Current.NetworkAccess == NetworkAccess.Internet; }
        catch { online = null; /* no MAUI host in unit tests */ }
        WelcomeInternetStatus = online switch
        {
            true => "Internet connected",
            false => "No internet connection",
            null => "Internet status unavailable",
        };
        WelcomeInternetDuration = Elapsed(sw);
        WelcomeInternetDone = online == true;
        WelcomeChecksFinished = 2;
        ProgressPercent = 40;

        sw.Restart();
        var hasProfile = !string.IsNullOrWhiteSpace(EmployeeFullName);
        WelcomeProfileStatus = hasProfile ? "Profile loaded" : "Profile details not found on this device";
        WelcomeProfileDuration = Elapsed(sw);
        WelcomeProfileDone = hasProfile;
        WelcomeChecksFinished = 3;
        ProgressPercent = 60;

        sw.Restart();
        var hasPolicy = _pipe?.LastKnownPolicy is not null
            || (_pipe is not null && serverUp && await WaitForAsync(() => _pipe.LastKnownPolicy is not null, ct));
        WelcomePoliciesCheckStatus = hasPolicy ? "Workspace policy applied" : "Waiting for workspace policy";
        WelcomePoliciesDuration = Elapsed(sw);
        WelcomePoliciesDone = hasPolicy;
        WelcomeChecksFinished = 4;
        ProgressPercent = 80;

        sw.Restart();
        WelcomeWorkspaceDuration = Elapsed(sw);
        WelcomeWorkspaceDone = true;
        ProgressPercent = 100;
        WelcomeChecksFinished = 5;
    }

    private async Task<bool> IsServiceEnrolledAsync(CancellationToken ct)
    {
        if (_pipe is null)
            return false;
        if (_pipe.LastKnownStatus is null)
            await WaitForAsync(() => _pipe.LastKnownStatus is not null, ct);
        return _pipe.LastKnownStatus is { State: not MonitoringState.Unenrolled };
    }

    private async Task<bool> WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ServiceWaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(100, ct);
        }
        return condition();
    }

    private static string Elapsed(Stopwatch sw) =>
        $"{sw.Elapsed.TotalSeconds:0.0}s";

    partial void OnStageChanged(string value)
    {
        RaiseSettingStepUi();
        RaiseWelcomeStepUi();
    }

    partial void OnActivationVerifiedChanged(bool value) => RaiseSettingStepUi();
    partial void OnUserDetailsFetchedChanged(bool value) => RaiseSettingStepUi();
    partial void OnSettingChecksFinishedChanged(bool value) => RaiseSettingStepUi();
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
