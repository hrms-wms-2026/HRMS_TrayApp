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
    [ObservableProperty] private string _connectionStatus = "Online";
    [ObservableProperty] private string _internetStatus   = "Excellent Connection";
    [ObservableProperty] private string _deviceType       = "Windows Desktop";
    [ObservableProperty] private bool   _isConnected      = true;

    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private bool _isSigningOut;
    [ObservableProperty] private string? _errorMessage;

    private AgentPolicy? _currentPolicy;

    public ClockInViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title       = "Ready to Start Work";
        _pipe       = pipe;
        _preferences = preferences;
        Greeting    = GetGreeting();
        _currentPolicy = pipe.LastKnownPolicy;
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

        _pipe.OnDisconnected += OnDisconnected;
        _pipe.OnStateReceived += _ =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsConnected = true;
                    ConnectionStatus = "Online";
                });
            }
            catch { /* unit tests */ }
        };
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
    }

    private void LoadEmployeeName()
    {
        // Enrollment saves the real name; fall back to Windows username.
        var fallbackName = string.IsNullOrWhiteSpace(Environment.UserName) ? "Employee" : Environment.UserName;
        try { EmployeeName = _preferences.Get(SessionPreferenceKeys.EmployeeDisplayName, fallbackName); }
        catch { EmployeeName = fallbackName; }
    }

    private void OnDisconnected()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = false;
                ConnectionStatus = "Offline";
            });
        }
        catch { /* unit tests */ }
    }

    private void TickClock()
    {
        void Apply()
        {
            CurrentDate = DateTimeOffset.Now;
            CurrentDateDisplay = CurrentDate.ToString("dddd, MMMM d, yyyy");
            CurrentTimeDisplay = CurrentDate.ToString("hh:mm tt");
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

    private void HandlePolicyReceived(AgentPolicy policy) => _currentPolicy = policy;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        try { MainThread.BeginInvokeOnMainThread(() => ApplyNetworkAccess(e.NetworkAccess)); }
        catch { ApplyNetworkAccess(e.NetworkAccess); }
    }

    private void ApplyNetworkAccess(NetworkAccess access)
    {
        InternetStatus = access switch
        {
            NetworkAccess.Internet => "Excellent Connection",
            NetworkAccess.ConstrainedInternet => "Limited Connection",
            NetworkAccess.Local => "No Internet Access",
            _ => "No Connection"
        };
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good Morning" : hour < 17 ? "Good Afternoon" : "Good Evening";
    }

    [RelayCommand]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            // Camera verification required — photo page completes the lifecycle command.
            if (_currentPolicy?.CameraVerificationEnabled == true)
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
                ErrorMessage = result.ErrorCode ?? "Sign-out failed.";
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
        _pipe.OnDisconnected   -= OnDisconnected;
        _pipe.OnPolicyReceived -= HandlePolicyReceived;
        try { Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged; }
        catch { /* unit tests */ }
        _clockTimer.Stop();
        _clockTimer.Dispose();
    }
}
