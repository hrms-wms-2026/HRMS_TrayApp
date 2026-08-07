namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed record TopAppItem(string Name, string Duration);

public sealed partial class EndSessionViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private bool _subscribed;

    [ObservableProperty] private string _clockInDisplay     = "—";
    [ObservableProperty] private string _clockOutDisplay    = "—";
    [ObservableProperty] private string _totalShiftDisplay  = "00:00:00";
    [ObservableProperty] private string _breakTimeDisplay   = "00:00:00";
    [ObservableProperty] private string _workingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00:00";
    [ObservableProperty] private string _idleTimeDisplay    = "00:00:00";
    [ObservableProperty] private string _breakSessionsDisplay = "0";
    [ObservableProperty] private string _statusText         = "Clocked Out";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<TopAppItem> TopApps { get; } = [];

    public EndSessionViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics)
    {
        Title = "Workday Completed";
        _pipe = pipe;
        _dayMetrics = dayMetrics;
        Message = "Here is your daily monitoring summary for today.";
    }

    /// <summary>Test helper.</summary>
    public EndSessionViewModel(INamedPipeClient pipe)
        : this(pipe, new SessionDayMetrics()) { }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            _subscribed = true;
        }

        // Prefer last completed session from Clock Out (most accurate).
        if (_dayMetrics.LastCompletedSession is { } completed)
            LoadFromSnapshot(completed);
        else if (_pipe.LastKnownStatus?.Session is { } cached)
            LoadFromSnapshot(cached);

        RefreshTopAppsAndIdle();

        _ = _pipe.SendEnvelopeAsync(
            new IpcEnvelope { Type = IpcMessageTypes.StatusRequest },
            CancellationToken.None);
    }

    public void OnDisappearing()
    {
        if (_subscribed)
        {
            _pipe.OnStatusReceived -= OnStatus;
            _subscribed = false;
        }
    }

    private void OnStatus(StatusResponsePayload status)
    {
        // After clock-out state is Stopped; still load session if present.
        if (status.Session is null) return;

        // Don't wipe a good completed snapshot with an empty/live one missing clock-out.
        if (status.Session.ClockOutAt is null
            && _dayMetrics.LastCompletedSession is { ClockOutAt: not null })
            return;

        void Apply()
        {
            LoadFromSnapshot(status.Session);
            RefreshTopAppsAndIdle();
        }

        try
        {
            if (MainThread.IsMainThread) Apply();
            else MainThread.BeginInvokeOnMainThread(Apply);
        }
        catch { Apply(); }
    }

    public void LoadFromSnapshot(SessionSnapshot session)
    {
        if (session.ClockInAt is not null)
            ClockInDisplay = session.ClockInAt.Value.ToLocalTime().ToString("hh:mm tt");
        else
            ClockInDisplay = "—";

        if (session.ClockOutAt is not null)
            ClockOutDisplay = session.ClockOutAt.Value.ToLocalTime().ToString("hh:mm tt");
        else
            ClockOutDisplay = "—";

        var breakTime = session.AccumulatedBreak < TimeSpan.Zero
            ? TimeSpan.Zero
            : session.AccumulatedBreak;
        var workTime = session.AccumulatedWork < TimeSpan.Zero
            ? TimeSpan.Zero
            : session.AccumulatedWork;

        // Recompute work from anchors if service sent zero but times exist.
        if (workTime == TimeSpan.Zero
            && session.ClockInAt is not null
            && session.ClockOutAt is not null)
        {
            var wall = session.ClockOutAt.Value - session.ClockInAt.Value;
            if (wall < TimeSpan.Zero) wall = TimeSpan.Zero;
            workTime = wall - breakTime;
            if (workTime < TimeSpan.Zero) workTime = TimeSpan.Zero;
        }

        BreakTimeDisplay   = Format(breakTime);
        WorkingTimeDisplay = Format(workTime);
        BreakSessionsDisplay = Math.Max(0, session.BreakSessionCount).ToString();

        if (session.ClockInAt is not null && session.ClockOutAt is not null)
        {
            var shift = session.ClockOutAt.Value - session.ClockInAt.Value;
            if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;
            TotalShiftDisplay = Format(shift);
        }
        else
        {
            TotalShiftDisplay = Format(workTime + breakTime);
        }

        // Productive ≈ work minus measured idle (clamped).
        var idle = _dayMetrics.TotalIdle;
        if (idle > workTime) idle = workTime;
        var productive = workTime - idle;
        if (productive < TimeSpan.Zero) productive = TimeSpan.Zero;

        IdleTimeDisplay       = Format(idle);
        ProductiveTimeDisplay = Format(productive);

        StatusText = "Clocked Out";
        Message    = "Here is your daily monitoring summary for today.";

        RefreshTopAppsAndIdle();
    }

    private void RefreshTopAppsAndIdle()
    {
        TopApps.Clear();
        var top = _dayMetrics.GetTopApps(5);
        if (top.Count == 0)
        {
            TopApps.Add(new TopAppItem("No app activity yet", "00:00:00"));
            return;
        }

        foreach (var (name, duration) in top)
            TopApps.Add(new TopAppItem(PrettyProcessName(name), Format(duration)));
    }

    private static string PrettyProcessName(string processName)
    {
        var n = processName.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        if (n.Length == 0) return "Unknown";
        return char.ToUpperInvariant(n[0]) + n[1..];
    }

    /// <summary>Legacy helper used by older tests / call sites.</summary>
    public void LoadSummary(DateTimeOffset clockIn, DateTimeOffset clockOut,
        TimeSpan breakTime, TimeSpan afkTime, TimeSpan meetingTime)
    {
        LoadFromSnapshot(new SessionSnapshot(
            ClockInAt: clockIn,
            ClockOutAt: clockOut,
            IsOnBreak: false,
            CurrentBreakStartedAt: null,
            AccumulatedBreak: breakTime,
            AccumulatedWork: (clockOut - clockIn) - breakTime - afkTime,
            ScheduleDisplay: null,
            BreakSessionCount: breakTime > TimeSpan.Zero ? 1 : 0));
        _ = meetingTime;
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

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
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task DownloadSummaryAsync()
    {
        try
        {
            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(downloads, "Downloads");
            if (!Directory.Exists(dir))
                dir = downloads;

            var path = Path.Combine(dir, $"Onexso-Workday-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("Onexso WorkPulse — Daily Work Summary");
            sb.AppendLine($"Status: {StatusText}");
            sb.AppendLine($"Clock In:  {ClockInDisplay}");
            sb.AppendLine($"Clock Out: {ClockOutDisplay}");
            sb.AppendLine($"Total Shift: {TotalShiftDisplay}");
            sb.AppendLine($"Working: {WorkingTimeDisplay}");
            sb.AppendLine($"Break: {BreakTimeDisplay}");
            sb.AppendLine($"Productive: {ProductiveTimeDisplay}");
            sb.AppendLine($"Idle: {IdleTimeDisplay}");
            sb.AppendLine($"Break Sessions: {BreakSessionsDisplay}");
            sb.AppendLine("Top Apps:");
            foreach (var app in TopApps)
                sb.AppendLine($"  - {app.Name}: {app.Duration}");
            await File.WriteAllTextAsync(path, sb.ToString());
            Message = $"Summary saved to {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private static void CloseApp()
    {
        try { Shell.Current.GoToAsync("//clockin"); }
        catch { /* unit tests */ }
    }
}
