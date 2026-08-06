namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ClockInViewModel : BaseViewModel, IDisposable
{
    private readonly INamedPipeClient _pipe;
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
    [ObservableProperty] private string? _errorMessage;

    public ClockInViewModel(INamedPipeClient pipe)
    {
        Title    = "Ready to Start Work";
        _pipe    = pipe;
        Greeting = GetGreeting();
        // Enrollment saves the real name; fall back to Windows username.
        EmployeeName = Preferences.Get("onevo.employee_display_name",
            string.IsNullOrWhiteSpace(Environment.UserName) ? "Employee" : Environment.UserName);

        _clockTimer = new System.Timers.Timer(1_000) { AutoReset = true };
        _clockTimer.Elapsed += (_, _) => TickClock();
        TickClock();
        _clockTimer.Start();

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
            LiveTimer = CurrentDate.ToString("HH:mm:ss");
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
            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, ct);
            if (result is null)
            {
                ErrorMessage = "No response from OneVo Agent Service. Is the service running?";
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

    public void Dispose()
    {
        _pipe.OnDisconnected -= OnDisconnected;
        _clockTimer.Stop();
        _clockTimer.Dispose();
    }
}
