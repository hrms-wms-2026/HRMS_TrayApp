namespace ONEVO.Agent.TrayApp.ViewModels;

using Microsoft.Maui.Networking;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ClockInViewModel : BaseViewModel, IDisposable
{
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _preferences;
    private readonly System.Timers.Timer _clockTimer;

    [ObservableProperty] private string _greeting         = "Good morning";
    [ObservableProperty] private string _employeeName     = string.Empty;
    [ObservableProperty] private string _workLocation     = string.Empty;
    [ObservableProperty] private DateTimeOffset _currentDate = DateTimeOffset.Now;
    [ObservableProperty] private string _currentDateDisplay = string.Empty;
    [ObservableProperty] private string _currentTimeDisplay = string.Empty;

    [ObservableProperty] private string _liveTimer        = "00:00:00";
    [ObservableProperty] private string _workingStatus    = "Ready";
    [ObservableProperty] private string _connectionStatus = "Connecting…";
    [ObservableProperty] private string _internetStatus   = "Checking…";
    [ObservableProperty] private string _deviceType       = DeviceKind.Display();
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private string _workspaceStatus  = "Connecting…";
    [ObservableProperty] private string _policiesStatus   = "Waiting for policy";

    [ObservableProperty] private string _scheduleDisplay  = "Not configured";
    [ObservableProperty] private bool _isStartConfirmVisible;
    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private bool _isSigningOut;
    [ObservableProperty] private string? _errorMessage;

    private AgentPolicy? _currentPolicy;

    private readonly ServiceConnectionStatus _connection;

    public ClockInViewModel(INamedPipeClient pipe, IPreferencesStore preferences,
        ServiceConnectionStatus? connection = null)
    {
        Title       = "Ready to Start Work";
        _pipe       = pipe;
        _preferences = preferences;
        Greeting    = GetGreeting();
        _currentPolicy = pipe.LastKnownPolicy;
        if (_currentPolicy is not null)
        {
            ScheduleDisplay = _currentPolicy.ScheduleDisplay;
            PoliciesStatus = PolicyLoadedText;
        }
        _pipe.OnPolicyReceived += HandlePolicyReceived;
        LoadEmployeeName();

        _clockTimer = new System.Timers.Timer(1_000) { AutoReset = true };
        _clockTimer.Elapsed += (_, _) => TickClock();
        TickClock();
        _clockTimer.Start();

        try
        {
            ApplyNetworkAccess(Connectivity.Current.NetworkAccess);
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }
        catch { /* no MAUI Connectivity host in unit tests — keep static default */ }

        // The shared tracker has seen every pipe transition since startup; LastKnownStatus is
        // never cleared on disconnect, so it can't tell us whether the Service is up right now.
        _connection = connection ?? new ServiceConnectionStatus(pipe);
        ApplyConnection();
        _connection.PropertyChanged += OnConnectionChanged;
    }

    private const string PolicyLoadedText = "Policy loaded";

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyConnection();

    private void ApplyConnection()
    {
        IsConnected = _connection.IsConnected;
        ConnectionStatus = _connection.Label;
        WorkspaceStatus = _connection.Label;
    }

    public void OnAppearing()
    {
        Greeting = GetGreeting();
        TickClock();
        if (!_clockTimer.Enabled)
            _clockTimer.Start();
        // ClockInPage is a cached Shell tab reused across sign-out/re-activation
        // cycles, so the employee name must be re-read here, not only at
        // construction, or the previous employee's name survives sign-out.
        LoadEmployeeName();
        WorkLocation = EmployeeSession.WorkLocation(_preferences, _currentPolicy);
        DeviceType = DeviceKind.Display();
    }

    private void LoadEmployeeName()
    {
        // Enrollment saves the real name; fall back to Windows username.
        var fallbackName = string.IsNullOrWhiteSpace(Environment.UserName) ? "Employee" : Environment.UserName;
        try { EmployeeName = EmployeeSession.Name(_preferences, fallbackName); }
        catch { EmployeeName = fallbackName; }
    }

    private void TickClock()
    {
        void Apply()
        {
            CurrentDate = DateTimeOffset.Now;
            CurrentDateDisplay = CurrentDate.ToString("MMMM d, yyyy");
            CurrentTimeDisplay = CurrentDate.ToString("hh:mm:ss tt");
            // Mockup: Live Timer stays at 00:00:00 until the employee clocks in.
            LiveTimer = "00:00:00";
        }

        try
        {
            if (MainThread.IsMainThread) Apply();
            else MainThread.BeginInvokeOnMainThread(Apply);
        }
        catch
        {
            Apply();
        }
    }

    private void HandlePolicyReceived(AgentPolicy policy)
    {
        _currentPolicy = policy;
        void Apply()
        {
            ScheduleDisplay = policy.ScheduleDisplay;
            PoliciesStatus = PolicyLoadedText;
        }
        try { MainThread.BeginInvokeOnMainThread(Apply); }
        catch { Apply(); }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        try { MainThread.BeginInvokeOnMainThread(() => ApplyNetworkAccess(e.NetworkAccess)); }
        catch { ApplyNetworkAccess(e.NetworkAccess); }
    }

    private void ApplyNetworkAccess(NetworkAccess access)
    {
        InternetStatus = access switch
        {
            NetworkAccess.Internet => "Online",
            NetworkAccess.ConstrainedInternet => "Limited Connection",
            NetworkAccess.Local => "No Internet Access",
            _ => "No Connection"
        };
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good Morning," : hour < 17 ? "Good Afternoon," : "Good Evening,";
    }

    [RelayCommand]
    private void RequestStartWorkday()
    {
        ErrorMessage = null;
        IsStartConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelStartWorkday()
    {
        IsStartConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmStartWorkdayAsync(CancellationToken ct)
    {
        IsStartConfirmVisible = false;
        await ClockInAsync(ct);
    }

    [RelayCommand]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            // Work mode "Photo required" (clock-in policy) — photo page completes Clock In.
            if (_currentPolicy?.PhotoRequiredEnabled == true)
            {
                try { await Shell.Current.GoToAsync("//photo?context=clockin"); }
                catch { /* unit tests */ }
                return;
            }

            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, ct);
            if (result is null)
            {
                ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
                return;
            }

            if (result.ErrorCode == ClockActionErrorCodes.AlreadyClockedOut)
            {
                // Today is already finished — show the finished-day screen, not an error.
                try { await Shell.Current.GoToAsync(SetupFlow.End); }
                catch { /* unit tests */ }
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.Message
                    ?? result.ErrorCode
                    ?? "Clock-in failed.";
                return;
            }

            try
            {
                await Shell.Current.GoToAsync("//active");
            }
            catch
            {
                // Shell may not be ready in unit tests.
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsClockinIn = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken ct)
    {
        if (IsSigningOut) return;
        IsSigningOut = true;
        ErrorMessage = null;
        try
        {
            var result = await _pipe.SendLogoutAsync(ct);
            if (result is null)
            {
                ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.ErrorCode switch
                {
                    "CLOCKED_IN" => "You're still clocked in. Clock out before signing out.",
                    null => "Sign-out failed.",
                    var code => code
                };
                return;
            }

            // Remove all employee/setup/session values, not only the three
            // greeting fields. This prevents the next activation from inheriting
            // location, coordinates, or face-verification state.
            SessionPreferenceKeys.ClearAll(_preferences);

            try { await Shell.Current.GoToAsync("//connect"); }
            catch { /* unit tests */ }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSigningOut = false;
        }
    }

    public void Dispose()
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        _pipe.OnPolicyReceived -= HandlePolicyReceived;
        try { Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged; }
        catch { /* unit tests */ }
        _clockTimer.Stop();
        _clockTimer.Dispose();
    }
}
