namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Diagnostics;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ActiveSessionViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly ICollectorLifecycleCoordinator _lifecycleCoordinator;
    private IDispatcherTimer? _uiTimer;
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
    // No tasks feature exists yet — show "—" rather than a fabricated count (see architecture §21).
    [ObservableProperty] private string _tasksCompletedDisplay = "—";
    [ObservableProperty] private bool   _isOnBreak;
    [ObservableProperty] private bool   _isBreakConfirmVisible;
    [ObservableProperty] private bool   _isBusyAction;
    [ObservableProperty] private string? _syncMessage;
    [ObservableProperty] private string? _errorMessage;

    public ActiveSessionViewModel(
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics,
        ICollectorLifecycleCoordinator lifecycleCoordinator)
    {
        Title = "Active Session";
        _pipe = pipe;
        _dayMetrics = dayMetrics;
        _lifecycleCoordinator = lifecycleCoordinator;
    }

    /// <summary>Test helper — empty day metrics, no-op collector-lifecycle drain.</summary>
    public ActiveSessionViewModel(INamedPipeClient pipe)
        : this(pipe, new SessionDayMetrics(), NoOpCollectorLifecycleCoordinator.Instance) { }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            _subscribed = true;
        }

        if (_pipe.LastKnownStatus is { } cached)
            ApplySession(cached.Session, cached.State == MonitoringState.Paused);

        _ = _pipe.SendEnvelopeAsync(
            new IpcEnvelope { Type = IpcMessageTypes.StatusRequest },
            CancellationToken.None);

        EnsureUiTimerRunning();
        UpdateTimersCore();
    }

    public void OnDisappearing()
    {
        // Keep UI timer running so values stay fresh if user returns quickly.
    }

    private void EnsureUiTimerRunning()
    {
        try
        {
            if (_uiTimer is not null)
            {
                if (!_uiTimer.IsRunning)
                    _uiTimer.Start();
                return;
            }

            var dispatcher = Application.Current?.Dispatcher
                             ?? Dispatcher.GetForCurrentThread();
            if (dispatcher is null)
                return;

            _uiTimer = dispatcher.CreateTimer();
            _uiTimer.Interval = TimeSpan.FromSeconds(1);
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();
        }
        catch
        {
            // Unit tests / headless — UpdateTimersCore still called after ApplySession.
        }
    }

    private void OnUiTimerTick(object? sender, EventArgs e) => UpdateTimersCore();

    private void OnStatus(StatusResponsePayload status)
    {
        ApplySession(status.Session, status.State == MonitoringState.Paused);
    }

    public void ApplySession(SessionSnapshot? session, bool? isOnBreakOverride = null)
    {
        if (session is null)
            return;

        void Apply()
        {
            _clockInAt = NormalizeUtc(session.ClockInAt);
            // AccumulatedBreak = closed breaks only (service contract).
            _accumulatedBreak = session.AccumulatedBreak < TimeSpan.Zero
                ? TimeSpan.Zero
                : session.AccumulatedBreak;
            _currentBreakStartedAt = NormalizeUtc(session.CurrentBreakStartedAt);
            _breakSessionCount = session.BreakSessionCount;
            IsOnBreak = isOnBreakOverride ?? session.IsOnBreak;

            // Recover if service said on-break but forgot break start timestamp.
            if (IsOnBreak && _currentBreakStartedAt is null)
                _currentBreakStartedAt = DateTimeOffset.UtcNow;

            if (_clockInAt is not null)
                StartTimeDisplay = _clockInAt.Value.ToLocalTime().ToString("hh:mm tt");

            if (!string.IsNullOrWhiteSpace(session.ScheduleDisplay))
                ScheduleDisplay = session.ScheduleDisplay!;

            ApplyModeChrome();
            EnsureUiTimerRunning();
            UpdateTimersCore();
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

    private static DateTimeOffset? NormalizeUtc(DateTimeOffset? value)
    {
        if (value is null) return null;
        // Ensure wall-clock math is always UTC-based.
        return value.Value.ToUniversalTime();
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

    partial void OnIsOnBreakChanged(bool value)
    {
        ApplyModeChrome();
        UpdateTimersCore();
    }

    /// <summary>Recompute all timer strings from clock-in / break anchors (UTC).</summary>
    public void UpdateTimersCore()
    {
        var now = DateTimeOffset.UtcNow;

        // Open break duration (this segment).
        TimeSpan openBreak = TimeSpan.Zero;
        if (IsOnBreak)
        {
            if (_currentBreakStartedAt is null)
                _currentBreakStartedAt = now;

            openBreak = now - _currentBreakStartedAt.Value;
            if (openBreak < TimeSpan.Zero)
                openBreak = TimeSpan.Zero;
        }

        var breakTotal = _accumulatedBreak + openBreak;
        if (breakTotal < TimeSpan.Zero)
            breakTotal = TimeSpan.Zero;

        TimeSpan work = TimeSpan.Zero;
        if (_clockInAt is not null)
        {
            var wall = now - _clockInAt.Value;
            if (wall < TimeSpan.Zero)
                wall = TimeSpan.Zero;
            work = wall - breakTotal;
            if (work < TimeSpan.Zero)
                work = TimeSpan.Zero;
        }

        WorkDurationDisplay   = Format(work);
        BreakTimeDisplay      = Format(breakTotal);
        ProductiveTimeDisplay = Format(work);

        // Primary big timer: break segment while on break, else work (live shift).
        PrimaryTimer = Format(IsOnBreak ? openBreak : work);
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
        var result = await RunLifecycleWithPreStopAsync(LifecycleAction.StartBreak, ct);
        if (IsStaleSessionError(result))
        {
            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task EndBreakAsync(CancellationToken ct)
    {
        // EndBreak resumes monitoring rather than pausing it, so it does not go through the
        // pre-stop drain — collectors are already stopped for the break's duration.
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
        // Navigation on success is owned entirely by App.xaml.cs's state-driven router
        // (fed by OnStateReceived/OnStatusReceived) — not here. Two navigation sources
        // racing on the same transition was the cause of the "//end" screen sometimes
        // rendering before RememberCompletedSession had run.
        var result = await RunLifecycleWithPreStopAsync(LifecycleAction.ClockOut, ct);
        if (result?.Success == true)
            return;

        if (IsStaleSessionError(result))
        {
            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { }
        }
    }

    /// <summary>
    /// Drains collectors (dismissing any pending inactivity prompt and waiting for an already
    /// approved capture + attempt IPC acknowledgement) before sending a pausing lifecycle command,
    /// so the Service durably enqueues the evidence attempt before it enqueues work-session
    /// completion. If the Service rejects the command while the authoritative state it returned is
    /// still Active, collectors are reconciled back on.
    /// </summary>
    private async Task<LifecycleResultPayload?> RunLifecycleWithPreStopAsync(LifecycleAction action, CancellationToken ct)
    {
        try
        {
            await _lifecycleCoordinator.PrepareForPauseAsync(ct);
        }
        catch
        {
            // Best-effort drain — a stuck local collector drain must never block the employee from
            // clocking out/starting a break. The lifecycle command below still proceeds.
        }

        var result = await RunLifecycleAsync(action, ct);

        if (result is { Success: false, State: MonitoringState.Active })
        {
            try { await _lifecycleCoordinator.ResumeAfterRejectedPauseAsync(ct); }
            catch { /* best-effort reconciliation */ }
        }

        return result;
    }

    [RelayCommand]
    private static void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://app.onexsoworkspace.com/dashboard",
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
                ErrorMessage = "No response from OneXso Agent Service.";
                return null;
            }

            if (!result.Success)
            {
                ErrorMessage = result.Message ?? result.ErrorCode ?? "Action failed.";
                return result;
            }

            SyncMessage = result.Message;
            ApplySession(result.Session, result.State == MonitoringState.Paused);

            // Persist completed session for End page (real totals, not zeros).
            if (action == LifecycleAction.ClockOut && result.Session is { } done)
                _dayMetrics.RememberCompletedSession(done);

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

        if (_uiTimer is not null)
        {
            try
            {
                _uiTimer.Stop();
                _uiTimer.Tick -= OnUiTimerTick;
            }
            catch { /* ignore */ }
            _uiTimer = null;
        }

        await Task.CompletedTask;
    }
}
