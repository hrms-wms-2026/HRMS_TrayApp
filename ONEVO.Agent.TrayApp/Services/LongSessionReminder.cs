namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

public interface ILongSessionToast
{
    void ShowLongSessionReminder(string title, string message, string buttonText, string action);
}

/// <summary>
/// Asks the employee what to do once a clock-in has stayed open for 24 hours (usually a forgotten
/// clock-out). Runs for the whole app lifetime, not on the Active Session page: that page's timer
/// stops while the window is hidden in the tray, which is exactly when this matters.
/// </summary>
/// <remarks>
/// Notify only. The toast never clocks out by itself: tray clock-out needs the collector drain, the
/// optional clock-out photo and no open break, so the button opens the existing confirmation instead.
/// One reminder per clock-in (keyed on ClockInAt, not the calendar day, since such a session always
/// crosses midnight). A tray restart past the mark shows it once more.
/// </remarks>
public sealed class LongSessionReminder : IDisposable
{
    public static readonly TimeSpan Threshold = TimeSpan.FromHours(24);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly INamedPipeClient _pipe;
    private readonly ILongSessionToast _toast;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly Timer? _timer;
    private DateTimeOffset? _openClockInAt;
    private DateTimeOffset? _remindedFor;
    private bool _clockOutRequested;

    /// <summary>Raised on the main thread after the employee picked "Clock out" on the toast.</summary>
    public event Action? ClockOutRequested;

    public LongSessionReminder(INamedPipeClient pipe, ILongSessionToast toast)
        : this(pipe, toast, () => DateTimeOffset.UtcNow, startTimer: true)
    {
    }

    internal LongSessionReminder(INamedPipeClient pipe, ILongSessionToast toast, Func<DateTimeOffset> now, bool startTimer)
    {
        _pipe = pipe;
        _toast = toast;
        _now = now;
        _pipe.OnStatusReceived += OnStatus;
        _pipe.OnDisconnected += OnDisconnected;
        // Wall-clock comparison on a coarse tick: a single 24 h due time would drift across sleep.
        if (startTimer)
            _timer = new Timer(_ => Check(), null, CheckInterval, CheckInterval);
    }

    public static bool IsOverdue(DateTimeOffset clockInAt, DateTimeOffset now) => now - clockInAt >= Threshold;

    private static void BootLog(string message) => TrayBootLog.Write("LongSession", message);

    private void OnStatus(StatusResponsePayload status)
    {
        lock (_gate)
        {
            _openClockInAt = status.State is MonitoringState.Active or MonitoringState.Paused
                ? status.Session?.ClockInAt?.ToUniversalTime()
                : null;
        }
        Check();
    }

    // Without the Service the tray cannot tell whether the session is still open: say nothing.
    private void OnDisconnected()
    {
        lock (_gate)
            _openClockInAt = null;
    }

    internal void Check()
    {
        DateTimeOffset clockInAt;
        lock (_gate)
        {
            if (_openClockInAt is not { } open || _remindedFor == open || !IsOverdue(open, _now()))
                return;
            _remindedFor = open;
            clockInAt = open;
        }

        var trayClockOut = _pipe.LastKnownPolicy?.TrayClockInEnabled ?? false;
        var since = clockInAt.ToLocalTime().ToString("ddd d MMM, hh:mm tt");
        var message = trayClockOut
            ? $"You clocked in on {since} and are still clocked in after 24 hours. Did you forget to clock out?"
            : $"You clocked in on {since} and are still clocked in after 24 hours. If you have finished work, clock out in the portal.";

        BootLog("24h clock-in reminder shown");
        _toast.ShowLongSessionReminder(
            "Still clocked in?",
            message,
            trayClockOut ? "Clock out" : "Open portal",
            trayClockOut ? LongSessionActivation.ClockOut : LongSessionActivation.OpenPortal);
    }

    /// <summary>Handles a click on the reminder toast (body or button). Never throws.</summary>
    public void HandleActivation(string? args)
    {
        var action = LongSessionActivation.TryGetAction(args);
        BootLog($"Reminder clicked action={action ?? "none"}");
        if (action == LongSessionActivation.OpenPortal)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = WorkspaceLinks.DashboardUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                BootLog($"Opening the portal failed: {ex.Message}");
            }
            return;
        }

        if (action != LongSessionActivation.ClockOut)
            return;

        bool sessionOpen;
        lock (_gate)
            sessionOpen = _openClockInAt is not null;

        TrayIconService.ShowMainWindow();
        // Already clocked out elsewhere since the toast: just show the window, the state router
        // has put it on the right page.
        if (!sessionOpen)
            return;

        try
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await Shell.Current.GoToAsync("//active"); }
                catch (Exception ex) { BootLog($"Navigating to //active failed: {ex.Message}"); }
                RequestClockOut();
            });
        }
        catch (Exception ex)
        {
            BootLog($"Reminder click dispatch failed: {ex.Message}");
        }
    }

    /// <summary>Marks a pending clock-out request and tells any live Active Session page about it.</summary>
    internal void RequestClockOut()
    {
        lock (_gate)
            _clockOutRequested = true;
        ClockOutRequested?.Invoke();
    }

    /// <summary>
    /// True once per toast click. The Active Session page calls this when the event fires and again
    /// when it appears, so a page created by the navigation still opens the confirmation.
    /// </summary>
    public bool TryConsumeClockOutRequest()
    {
        lock (_gate)
        {
            if (!_clockOutRequested)
                return false;
            _clockOutRequested = false;
            return true;
        }
    }

    public void Dispose()
    {
        _pipe.OnStatusReceived -= OnStatus;
        _pipe.OnDisconnected -= OnDisconnected;
        _timer?.Dispose();
    }
}
