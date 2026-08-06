namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Diagnostics;
using System.Text;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class EndSessionViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
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

    public EndSessionViewModel(INamedPipeClient pipe)
    {
        Title = "Workday Completed";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            _subscribed = true;
        }

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
        if (status.Session is null) return;
        void Apply() => LoadFromSnapshot(status.Session);
        if (MainThread.IsMainThread) Apply();
        else MainThread.BeginInvokeOnMainThread(Apply);
    }

    public void LoadFromSnapshot(SessionSnapshot session)
    {
        if (session.ClockInAt is not null)
            ClockInDisplay = session.ClockInAt.Value.ToLocalTime().ToString("hh:mm tt");
        if (session.ClockOutAt is not null)
            ClockOutDisplay = session.ClockOutAt.Value.ToLocalTime().ToString("hh:mm tt");

        BreakTimeDisplay      = Format(session.AccumulatedBreak);
        WorkingTimeDisplay    = Format(session.AccumulatedWork);
        ProductiveTimeDisplay = Format(session.AccumulatedWork); // stub until productivity model
        IdleTimeDisplay       = "00:00:00";                      // stub
        BreakSessionsDisplay  = session.BreakSessionCount.ToString();

        if (session.ClockInAt is not null && session.ClockOutAt is not null)
        {
            var shift = session.ClockOutAt.Value - session.ClockInAt.Value;
            if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;
            TotalShiftDisplay = Format(shift);
        }

        StatusText = "Clocked Out";
        Message    = "Here is your daily monitoring summary for today.";
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
        _ = afkTime;
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
                FileName = "https://onevo.example.com/dashboard",
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

            var path = Path.Combine(dir, $"ONEVO-Workday-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("ONEVO WorkPulse — Daily Work Summary");
            sb.AppendLine($"Status: {StatusText}");
            sb.AppendLine($"Clock In:  {ClockInDisplay}");
            sb.AppendLine($"Clock Out: {ClockOutDisplay}");
            sb.AppendLine($"Total Shift: {TotalShiftDisplay}");
            sb.AppendLine($"Working: {WorkingTimeDisplay}");
            sb.AppendLine($"Break: {BreakTimeDisplay}");
            sb.AppendLine($"Break Sessions: {BreakSessionsDisplay}");
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
