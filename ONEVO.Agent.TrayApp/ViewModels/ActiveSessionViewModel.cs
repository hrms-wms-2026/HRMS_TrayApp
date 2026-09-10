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
    private readonly ILocationService _location;
    private IDispatcherTimer? _uiTimer;
    private DateTimeOffset? _clockInAt;
    private TimeSpan _accumulatedBreak;
    private DateTimeOffset? _currentBreakStartedAt;
    private TimeSpan _accumulatedIdle;
    private DateTimeOffset? _currentIdleStartedAt;
    private int _breakSessionCount;
    private bool _subscribed;

    [ObservableProperty] private string _headerTitle       = "You are now Clocked In!";
    [ObservableProperty] private string _headerLead        = "You are now";
    [ObservableProperty] private string _headerAccent      = "Clocked In";
    [ObservableProperty] private string _headerSubtitle    = "Have a productive and successful day ahead.";
    [ObservableProperty] private string _statusText        = "Working";
    [ObservableProperty] private string _primaryTimerLabel = "Live Shift Timer";
    [ObservableProperty] private string _primaryTimer      = "00:00:00";
    [ObservableProperty] private string _startTimeDisplay  = "—";
    [ObservableProperty] private string _scheduleDisplay   = "Not configured";
    [ObservableProperty] private string _workDurationDisplay = "00:00:00";
    [ObservableProperty] private string _breakTimeDisplay  = "00:00:00";
    [ObservableProperty] private string _idleTimeDisplay = "00:00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00:00";
    [ObservableProperty] private bool _isIdle;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWorkingActions))]
    [NotifyPropertyChangedFor(nameof(ShowClockOutAction))]
    private bool   _isOnBreak;
    [ObservableProperty] private bool   _isBreakConfirmVisible;
    [ObservableProperty] private bool   _isEndBreakConfirmVisible;
    [ObservableProperty] private bool   _isBusyAction;
    [ObservableProperty] private string? _syncMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _hintMessage = "";
    [ObservableProperty] private string _workLocationDisplay = "—";
    [ObservableProperty] private string _breakSessionsDisplay = "0";
    [ObservableProperty] private string _workStartedCaption = "";
    [ObservableProperty] private string _breakTotalCaption = "Total Break Time: 00:00:00";
    [ObservableProperty] private string _productiveShareCaption = "0% of work duration";

    // "Request location change" (remote work mode only).
    [ObservableProperty] private bool _isRemoteWorkMode;
    [ObservableProperty] private bool _isRequestLocationChangeFormVisible;
    [ObservableProperty] private string _locationChangeReason = string.Empty;
    [ObservableProperty] private bool _isSubmittingLocationChange;
    [ObservableProperty] private string? _locationChangeError;
    [ObservableProperty] private string? _locationChangeStatusMessage;

    // Post-clock-in "save this as your new location?" prompt.
    [ObservableProperty] private bool _isLocationChangePromptVisible;
    [ObservableProperty] private bool _isRespondingToLocationChangePrompt;
    private Guid? _pendingLocationChangeRequestId;

    public ActiveSessionViewModel(
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics,
        ICollectorLifecycleCoordinator lifecycleCoordinator,
        ILocationService location)
    {
        Title = "Active Session";
        _pipe = pipe;
        _dayMetrics = dayMetrics;
        _lifecycleCoordinator = lifecycleCoordinator;
        _location = location;
    }

    public bool ShowWorkingActions => !IsOnBreak;

    public bool ShowClockOutAction => ShowWorkingActions && (_pipe.LastKnownPolicy?.TrayClockInEnabled ?? false);

    /// <summary>Test helper — empty day metrics, no-op collector-lifecycle drain, no-op location capture.</summary>
    public ActiveSessionViewModel(INamedPipeClient pipe)
        : this(pipe, new SessionDayMetrics(), NoOpCollectorLifecycleCoordinator.Instance, NoOpLocationService.Instance) { }

    /// <summary>Test helper — no-op location capture (existing 3-arg call sites keep compiling).</summary>
    public ActiveSessionViewModel(
        INamedPipeClient pipe, ISessionDayMetrics dayMetrics, ICollectorLifecycleCoordinator lifecycleCoordinator)
        : this(pipe, dayMetrics, lifecycleCoordinator, NoOpLocationService.Instance) { }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            _pipe.OnPolicyReceived += OnPolicyReceivedForClockOutVisibility;
            _subscribed = true;
        }

        if (_pipe.LastKnownStatus is { } cached)
            ApplySession(cached.Session, cached.State == MonitoringState.Paused);

        _ = _pipe.SendEnvelopeAsync(
            new IpcEnvelope { Type = IpcMessageTypes.StatusRequest },
            CancellationToken.None);

        try
        {
            WorkLocationDisplay = EmployeeSession.FirstNonEmpty(
                Microsoft.Maui.Storage.Preferences.Get("onevo.work_location_display", string.Empty), "—");
        }
        catch { /* unit tests */ }

        try
        {
            IsRemoteWorkMode = string.Equals(
                Microsoft.Maui.Storage.Preferences.Get(SessionPreferenceKeys.WorkMode, string.Empty),
                "Remote", StringComparison.OrdinalIgnoreCase);
        }
        catch { IsRemoteWorkMode = false; }

        // Fires after every clock-in (this page is navigated to right after one) — drives the
        // "save this as your new location?" re-prompt per the approved-but-not-applied contract.
        // Deliberately unconditional (not gated on IsRemoteWorkMode, which only reflects the label
        // captured at enrollment/pairing time): the backend is the sole authority on eligibility —
        // GetPendingLocationChangeDecisionQueryHandler already checks the WorkLocationVerification
        // monitoring toggle and an approved request, so a Hybrid employee working remote today (per
        // the backend's own daily IExpectedWorkAreaResolver, not the stale enrollment label) still
        // gets prompted correctly even though the "Request Location Change" button stays hidden for them.
        _ = CheckPendingLocationChangeAsync();

        EnsureUiTimerRunning();
        UpdateTimersCore();
    }

    private async Task CheckPendingLocationChangeAsync()
    {
        try
        {
            var result = await _pipe.SendLocationChangePendingCheckAsync(CancellationToken.None);
            if (result is { Success: true, Request: not null })
            {
                _pendingLocationChangeRequestId = result.Request.Id;
                IsLocationChangePromptVisible = true;
            }
        }
        catch { /* best-effort — the next appearance/clock-in tries again */ }
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
            _accumulatedIdle = session.AccumulatedIdle < TimeSpan.Zero
                ? TimeSpan.Zero
                : session.AccumulatedIdle;
            _currentIdleStartedAt = NormalizeUtc(session.CurrentIdleStartedAt);
            IsIdle = session.IsIdle;
            if (IsIdle && _currentIdleStartedAt is null)
                _currentIdleStartedAt = DateTimeOffset.UtcNow;
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
            HeaderLead        = "You are now";
            HeaderAccent      = "On Break";
            HeaderSubtitle    = "Take a short break. You're doing great.";
            StatusText        = "On Break";
            PrimaryTimerLabel = "Break Timer";
            HintMessage       = "You'll be notified when your break time is ending.";
            SyncMessage       = null;
        }
        else
        {
            HeaderTitle       = "You are now Clocked In";
            HeaderLead        = "You are now";
            HeaderAccent      = "Clocked In";
            HeaderSubtitle    = "Your work session has started successfully.";
            StatusText        = "Working";
            PrimaryTimerLabel = "Live Shift Timer";
            HintMessage       = "Activity monitoring is now active.";
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

        TimeSpan openIdle = TimeSpan.Zero;
        if (IsIdle)
        {
            if (_currentIdleStartedAt is null)
                _currentIdleStartedAt = now;

            openIdle = now - _currentIdleStartedAt.Value;
            if (openIdle < TimeSpan.Zero)
                openIdle = TimeSpan.Zero;
        }

        var idleTotal = _accumulatedIdle + openIdle;
        if (idleTotal < TimeSpan.Zero)
            idleTotal = TimeSpan.Zero;

        TimeSpan wall = TimeSpan.Zero;
        if (_clockInAt is not null)
        {
            wall = now - _clockInAt.Value;
            if (wall < TimeSpan.Zero)
                wall = TimeSpan.Zero;
        }

        var work = wall - breakTotal - idleTotal;
        if (work < TimeSpan.Zero)
            work = TimeSpan.Zero;

        WorkDurationDisplay   = Format(work);
        BreakTimeDisplay      = Format(breakTotal);
        IdleTimeDisplay       = Format(idleTotal);
        ProductiveTimeDisplay = Format(work);
        BreakSessionsDisplay  = Math.Max(0, _breakSessionCount).ToString();
        WorkStartedCaption = string.IsNullOrWhiteSpace(StartTimeDisplay) || StartTimeDisplay == "—"
            ? string.Empty
            : $"Started at {StartTimeDisplay}";
        BreakTotalCaption = $"Total Break Time: {BreakTimeDisplay}";
        var share = wall.TotalSeconds <= 0
            ? 0
            : (int)Math.Round(100.0 * work.TotalSeconds / wall.TotalSeconds);
        ProductiveShareCaption = $"{share}% of work duration";

        // Primary big timer: break segment while on break, else total elapsed shift time
        // (wall clock since clock-in) so it equals Break + Productive + Idle.
        PrimaryTimer = Format(IsOnBreak ? openBreak : wall);
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
    private void RequestEndBreak()
    {
        if (!IsOnBreak || IsBusyAction) return;
        IsEndBreakConfirmVisible = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CancelEndBreakConfirm()
    {
        IsEndBreakConfirmVisible = false;
    }

    [RelayCommand]
    private async Task EndBreakAsync(CancellationToken ct)
    {
        IsEndBreakConfirmVisible = false;
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
        if (_pipe.LastKnownPolicy?.CameraVerificationEnabled == true)
        {
            try { await Shell.Current.GoToAsync("//photo?context=clockout"); }
            catch { /* unit tests */ }
            return;
        }

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
                FileName = WorkspaceLinks.DashboardUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if browser cannot open.
        }
    }

    [RelayCommand]
    private void RequestLocationChange()
    {
        if (!IsRemoteWorkMode) return;
        LocationChangeReason = string.Empty;
        LocationChangeError = null;
        LocationChangeStatusMessage = null;
        IsRequestLocationChangeFormVisible = true;
    }

    [RelayCommand]
    private void CancelRequestLocationChange()
    {
        IsRequestLocationChangeFormVisible = false;
    }

    [RelayCommand]
    private async Task SubmitLocationChangeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LocationChangeReason))
        {
            LocationChangeError = "Please enter a reason for the location change.";
            return;
        }

        IsSubmittingLocationChange = true;
        LocationChangeError = null;
        try
        {
            var fix = await _location.GetCurrentAsync(ct);
            if (!fix.IsSuccess)
            {
                LocationChangeError = "Could not detect your current location. Please retry.";
                return;
            }

            var result = await _pipe.SendLocationChangeSubmitAsync(
                fix.Fix!.Latitude, fix.Fix.Longitude, fix.Fix.AccuracyMeters, LocationChangeReason.Trim(), ct);

            if (result is null)
            {
                LocationChangeError = "No response from OneXso Agent Service. Is the service running?";
                return;
            }

            if (!result.Success)
            {
                LocationChangeError = result.ErrorCode switch
                {
                    "CONFLICT" => "You already have a pending or approved location change request.",
                    "UNENROLLED" => "Device is not enrolled.",
                    _ => "Could not submit your request. Please try again."
                };
                return;
            }

            IsRequestLocationChangeFormVisible = false;
            LocationChangeStatusMessage = "Your location change request has been submitted for approval.";
        }
        finally
        {
            IsSubmittingLocationChange = false;
        }
    }

    [RelayCommand]
    private Task ConfirmLocationChangePromptAsync(CancellationToken ct) =>
        RespondToLocationChangePromptAsync(apply: true, ct);

    [RelayCommand]
    private Task DismissLocationChangePromptAsync(CancellationToken ct) =>
        RespondToLocationChangePromptAsync(apply: false, ct);

    /// <summary>
    /// "No" is deliberately not terminal — the backend leaves the request Approved either way, so
    /// the next clock-in's CheckPendingLocationChangeAsync poll re-shows this same prompt until the
    /// employee says "Yes". This call is best-effort UX/logging, not what makes "no" non-terminal.
    /// </summary>
    private async Task RespondToLocationChangePromptAsync(bool apply, CancellationToken ct)
    {
        if (_pendingLocationChangeRequestId is not { } id)
        {
            IsLocationChangePromptVisible = false;
            return;
        }

        IsRespondingToLocationChangePrompt = true;
        try
        {
            await _pipe.SendLocationChangeRespondAsync(id, apply, ct);
        }
        catch { /* best-effort — re-prompts next clock-in either way */ }
        finally
        {
            IsRespondingToLocationChangePrompt = false;
            IsLocationChangePromptVisible = false;
            _pendingLocationChangeRequestId = null;
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

    private void OnPolicyReceivedForClockOutVisibility(AgentPolicy _) => OnPropertyChanged(nameof(ShowClockOutAction));

    public async ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _pipe.OnStatusReceived -= OnStatus;
            _pipe.OnPolicyReceived -= OnPolicyReceivedForClockOutVisibility;
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
