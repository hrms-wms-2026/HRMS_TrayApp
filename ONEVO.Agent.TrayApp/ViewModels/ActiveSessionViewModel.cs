namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Diagnostics;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ActiveSessionViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly INamedPipeClient _pipe;
    private readonly System.Timers.Timer _clockTimer;
    private DateTimeOffset? _clockInAt;
    private TimeSpan _accumulatedBreak;
    private DateTimeOffset? _currentBreakStartedAt;
    private int _breakSessionCount;
    private bool _subscribed;

    [ObservableProperty] private string _headerTitle       = "You are now Clocked In!";
    [ObservableProperty] private string _headerSubtitle    = "Have a productive and successful day ahead.";
    [ObservableProperty] private string _statusText        = "Working";
    [ObservableProperty] private string _primaryTimerLabel = "Live Shift Timer";
    [ObservableProperty] private string _primaryTimer      = "00:00:00";
    [ObservableProperty] private string _startTimeDisplay  = "—";
    [ObservableProperty] private string _scheduleDisplay   = "09:00 AM – 06:00 PM";
    [ObservableProperty] private string _workDurationDisplay = "00:00:00";
    [ObservableProperty] private string _breakTimeDisplay  = "00:00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00:00";
    [ObservableProperty] private string _tasksCompletedDisplay = "—";
    [ObservableProperty] private bool   _isOnBreak;
    [ObservableProperty] private bool   _isBreakConfirmVisible;
    [ObservableProperty] private bool   _isBusyAction;
    [ObservableProperty] private string? _syncMessage;
    [ObservableProperty] private string? _errorMessage;

    public ActiveSessionViewModel(INamedPipeClient pipe)
    {
        Title       = "Active Session";
        _pipe       = pipe;
        _clockTimer = new System.Timers.Timer(1_000) { AutoReset = true };
        _clockTimer.Elapsed += (_, _) => UpdateTimers();
    }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            _subscribed = true;
        }

        // Pull latest status so we resync after navigation.
        _ = _pipe.SendEnvelopeAsync(
            new IpcEnvelope { Type = IpcMessageTypes.StatusRequest },
            CancellationToken.None);

        if (!_clockTimer.Enabled)
            _clockTimer.Start();
    }

    public void OnDisappearing()
    {
        // Keep timer running while on break/working; only stop on dispose.
    }

    private void OnStatus(StatusResponsePayload status)
    {
        ApplySession(status.Session, status.State == ONEVO.Agent.Shared.Models.MonitoringState.Paused);
    }

    public void ApplySession(SessionSnapshot? session, bool? isOnBreakOverride = null)
    {
        if (session is null)
            return;

        void Apply()
        {
            _clockInAt = session.ClockInAt;
            _accumulatedBreak = session.AccumulatedBreak;
            _currentBreakStartedAt = session.CurrentBreakStartedAt;
            _breakSessionCount = session.BreakSessionCount;
            IsOnBreak = isOnBreakOverride ?? session.IsOnBreak;

            if (session.ClockInAt is not null)
                StartTimeDisplay = session.ClockInAt.Value.ToLocalTime().ToString("hh:mm tt");

            if (!string.IsNullOrWhiteSpace(session.ScheduleDisplay))
                ScheduleDisplay = session.ScheduleDisplay!;

            ApplyModeChrome();
            UpdateTimersCore();
        }

        // Unit tests have no MAUI main-thread dispatcher — apply inline when unavailable.
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

    private void ApplyModeChrome()
    {
        if (IsOnBreak)
        {
            HeaderTitle       = "You are now On Break";
            HeaderSubtitle    = "Take a short break. You're doing great!";
            StatusText        = "On Break";
            PrimaryTimerLabel = "Break Timer";
        }
        else
        {
            HeaderTitle       = "You are now Clocked In!";
            HeaderSubtitle    = "Have a productive and successful day ahead.";
            StatusText        = "Working";
            PrimaryTimerLabel = "Live Shift Timer";
        }
    }

    partial void OnIsOnBreakChanged(bool value) => ApplyModeChrome();

    private void UpdateTimers()
    {
        try
        {
            if (MainThread.IsMainThread) UpdateTimersCore();
            else MainThread.BeginInvokeOnMainThread(UpdateTimersCore);
        }
        catch
        {
            // UI may be torn down.
        }
    }

    private void UpdateTimersCore()
    {
        var now = DateTimeOffset.UtcNow;
        var breakTotal = _accumulatedBreak;
        if (IsOnBreak && _currentBreakStartedAt is not null)
        {
            var open = now - _currentBreakStartedAt.Value;
            if (open > TimeSpan.Zero)
                breakTotal += open;
        }

        TimeSpan work = TimeSpan.Zero;
        if (_clockInAt is not null)
        {
            var wall = now - _clockInAt.Value;
            work = wall - breakTotal;
            if (work < TimeSpan.Zero)
                work = TimeSpan.Zero;
        }

        WorkDurationDisplay   = Format(work);
        BreakTimeDisplay      = Format(breakTotal);
        // Productive stub = work until idle/productivity model exists.
        ProductiveTimeDisplay = Format(work);

        if (IsOnBreak)
        {
            var liveBreak = TimeSpan.Zero;
            if (_currentBreakStartedAt is not null)
            {
                liveBreak = now - _currentBreakStartedAt.Value;
                if (liveBreak < TimeSpan.Zero) liveBreak = TimeSpan.Zero;
            }
            PrimaryTimer = Format(liveBreak);
        }
        else
        {
            PrimaryTimer = Format(work);
        }
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    [RelayCommand]
    private void RequestBreak()
    {
        if (IsOnBreak || IsBusyAction) return;
        IsBreakConfirmVisible = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CancelBreakConfirm()
    {
        IsBreakConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmStartBreakAsync(CancellationToken ct)
    {
        IsBreakConfirmVisible = false;
        var result = await RunLifecycleAsync(LifecycleAction.StartBreak, ct);
        if (IsStaleSessionError(result))
        {
            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task EndBreakAsync(CancellationToken ct)
    {
        var result = await RunLifecycleAsync(LifecycleAction.EndBreak, ct);
        if (IsStaleSessionError(result))
        {
            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task ClockOutAsync(CancellationToken ct)
    {
        var result = await RunLifecycleAsync(LifecycleAction.ClockOut, ct);
        if (result?.Success == true)
        {
            try { await Shell.Current.GoToAsync("//end"); }
            catch { /* unit tests */ }
            return;
        }
        // Service has no record of this session (e.g. reconnected to fresh service instance).
        // Return to clock-in instead of leaving the user stranded on this page.
        if (IsStaleSessionError(result))
        {
            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { }
        }
    }

    [RelayCommand]
    private static void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://onevo.example.com/dashboard",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if browser cannot open.
        }
    }

    private static bool IsStaleSessionError(LifecycleResultPayload? r) =>
        r is { Success: false } && (
            r.ErrorCode is "NO_ACTIVE_SESSION" or "NOT_CLOCKED_IN" or "SESSION_NOT_FOUND" ||
            r.Message?.Contains("active work session", StringComparison.OrdinalIgnoreCase) == true ||
            r.Message?.Contains("not clocked in", StringComparison.OrdinalIgnoreCase) == true);

    private async Task<LifecycleResultPayload?> RunLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct)
    {
        IsBusyAction = true;
        ErrorMessage = null;
        SyncMessage  = null;
        try
        {
            var result = await _pipe.SendLifecycleAsync(action, ct);
            if (result is null)
            {
                ErrorMessage = "No response from OneVo Agent Service.";
                return null;
            }

            if (!result.Success)
            {
                ErrorMessage = result.Message ?? result.ErrorCode ?? "Action failed.";
                return result;
            }

            SyncMessage = result.Message;
            ApplySession(result.Session, result.State == ONEVO.Agent.Shared.Models.MonitoringState.Paused);
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
        finally
        {
            IsBusyAction = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _pipe.OnStatusReceived -= OnStatus;
            _subscribed = false;
        }
        _clockTimer.Stop();
        _clockTimer.Dispose();
        await Task.CompletedTask;
    }
}
