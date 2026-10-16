namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed record TopAppItem(
    string Name,
    string Duration,
    ImageSource? IconSource = null,
    string Percent = "",
    string ColorHex = "#6366F1",
    double Fraction = 0,
    string DisplayDuration = "")
{
    public Color Swatch => Color.FromArgb(string.IsNullOrWhiteSpace(ColorHex) ? "#6366F1" : ColorHex);

    public string DurationText => string.IsNullOrWhiteSpace(DisplayDuration) ? Duration : DisplayDuration;
}

/// <summary>No-op icon cache for call sites that don't need real icons (unit tests).</summary>
public sealed class NullAppIconCache : IAppIconCache
{
    public static readonly NullAppIconCache Instance = new();
    public ImageSource? GetIcon(string processName) => null;
    public void TryCacheFromForegroundWindow(IntPtr hwnd, string processName) { }
}

public sealed partial class EndSessionViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly IAppIconCache _iconCache;
    private bool _subscribed;

    [ObservableProperty] private string _clockInDisplay     = "—";
    [ObservableProperty] private string _clockOutDisplay    = "—";
    [ObservableProperty] private string _totalShiftDisplay  = "00:00:00";
    [ObservableProperty] private string _breakTimeDisplay   = "00:00:00";
    [ObservableProperty] private string _workingTimeDisplay = "00:00:00";
    /// <summary>On-the-clock time excluding breaks (shift - break, so idle is included).</summary>
    [ObservableProperty] private string _workDurationDisplay = "00:00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00:00";
    [ObservableProperty] private string _idleTimeDisplay    = "00:00:00";
    [ObservableProperty] private string _breakSessionsDisplay = "0";
    [ObservableProperty] private string _statusText         = "Clocked Out";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string _greetingMessage = "Your workday is complete.";
    private int? _activeSharePercent;
    [ObservableProperty] private string _sessionEndedCaption = "Your work session has ended.";
    private DateTime? _sessionDay;
    [ObservableProperty] private string _savedAtDisplay = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _employeeName = string.Empty;
    [ObservableProperty] private string _savedTitle = "Checking saved data…";
    [ObservableProperty] private string _savedCaption = "Waiting for the WorkPulse service to confirm your records.";
    [ObservableProperty] private string _syncTitle = "Checking cloud sync…";
    [ObservableProperty] private string _syncCaption = "Waiting for the WorkPulse service to report sync status.";
    [ObservableProperty] private string _syncTimeDisplay = "";

    public ObservableCollection<TopAppItem> TopApps { get; } = [];

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public EndSessionViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics, IAppIconCache iconCache,
        ServiceConnectionStatus? connection = null)
    {
        Connection = connection ?? new ServiceConnectionStatus(pipe);
        Title = "Workday Completed";
        _pipe = pipe;
        _dayMetrics = dayMetrics;
        _iconCache = iconCache;
    }

    /// <summary>Test helper.</summary>
    public EndSessionViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics)
        : this(pipe, dayMetrics, NullAppIconCache.Instance) { }

    /// <summary>Test helper.</summary>
    public EndSessionViewModel(INamedPipeClient pipe)
        : this(pipe, new SessionDayMetrics(), NullAppIconCache.Instance) { }

    public void OnAppearing()
    {
        if (!_subscribed)
        {
            _pipe.OnStatusReceived += OnStatus;
            Connection.PropertyChanged += OnConnectionChanged;
            _subscribed = true;
        }

        if (Connection.IsConnected && _pipe.LastKnownStatus is { } last)
            ApplySyncStatus(last);
        else if (Connection.Label == "Offline")
            ApplyOffline();

        // Prefer last completed session from Clock Out (most accurate).
        if (_dayMetrics.LastCompletedSession is { } completed)
            LoadFromSnapshot(completed);
        else if (_pipe.LastKnownStatus?.Session is { } cached)
            LoadFromSnapshot(cached);

        try
        {
            EmployeeName = Microsoft.Maui.Storage.Preferences.Get("onevo.employee_display_name", string.Empty);
            ApplyGreeting();
        }
        catch { /* unit tests */ }

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
            Connection.PropertyChanged -= OnConnectionChanged;
            _subscribed = false;
        }
    }

    private void OnStatus(StatusResponsePayload status)
    {
        void Apply()
        {
            ApplySyncStatus(status);

            // After clock-out state is Stopped; still load session if present.
            if (status.Session is null) return;

            // Don't wipe a good completed snapshot with an empty/live one missing clock-out.
            if (status.Session.ClockOutAt is null
                && _dayMetrics.LastCompletedSession is { ClockOutAt: not null })
                return;

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

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServiceConnectionStatus.IsConnected) && !Connection.IsConnected)
            ApplyOffline();
    }

    private void ApplyOffline()
    {
        void Apply()
        {
            SavedTitle = "Save status unknown";
            SavedCaption = "The WorkPulse service is offline, so saved data can't be confirmed right now.";
            SavedAtDisplay = string.Empty;
            SyncTitle = "Cloud sync paused";
            SyncCaption = "The WorkPulse service is offline. Your data will sync once it reconnects.";
            SyncTimeDisplay = string.Empty;
        }

        try
        {
            if (MainThread.IsMainThread) Apply();
            else MainThread.BeginInvokeOnMainThread(Apply);
        }
        catch { Apply(); }
    }

    /// <summary>Cloud sync card reflects the Service's offline buffer: synced only when nothing is pending.</summary>
    public void ApplySyncStatus(StatusResponsePayload status)
    {
        var buffer = status.Buffer;
        if (buffer is null)
        {
            SavedTitle = "Save status unavailable";
            SavedCaption = "This WorkPulse service version doesn't report whether data was saved.";
            SavedAtDisplay = string.Empty;
            SyncTitle = "Sync status unavailable";
            SyncCaption = "This WorkPulse service version does not report cloud sync status.";
            SyncTimeDisplay = string.Empty;
            return;
        }

        var storageFailed = string.Equals(buffer.Level, "StorageError", StringComparison.OrdinalIgnoreCase);
        if (storageFailed)
        {
            SavedTitle = "Your work data could not be saved";
            SavedCaption = "The last write on this device failed. Contact IT if this keeps happening.";
            SavedAtDisplay = string.Empty;
        }
        else
        {
            SavedTitle = "Your work data has been securely saved.";
            SavedCaption = "All logs, activity, and timesheets are recorded.";
            SavedAtDisplay = status.Timestamp.ToLocalTime().ToString("hh:mm tt");
        }

        if (storageFailed)
        {
            SyncTitle = "Local save problem";
            SyncCaption = $"The last write to the offline queue failed. {buffer.Pending} record(s) waiting to sync.";
            SyncTimeDisplay = string.Empty;
            return;
        }

        if (buffer.Pending > 0)
        {
            SyncTitle = "Waiting to sync";
            SyncCaption = $"{buffer.Pending} record(s) are saved on this device and will upload automatically.";
            SyncTimeDisplay = string.Empty;
            return;
        }

        SyncTitle = "Synced to OneXso Cloud";
        SyncCaption = "Your data is backed up and available across your devices.";
        SyncTimeDisplay = status.Timestamp.ToLocalTime().ToString("hh:mm tt");
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
        var idle = session.AccumulatedIdle < TimeSpan.Zero
            ? TimeSpan.Zero
            : session.AccumulatedIdle;
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
            workTime = wall - breakTime - idle;
            if (workTime < TimeSpan.Zero) workTime = TimeSpan.Zero;
        }

        BreakTimeDisplay   = Format(breakTime);
        WorkingTimeDisplay = Format(workTime);
        IdleTimeDisplay    = Format(idle);
        var tracked = workTime + idle;
        _activeSharePercent = tracked.TotalMinutes < 1
            ? null
            : (int)Math.Round(100.0 * workTime.TotalSeconds / tracked.TotalSeconds);

        // Productive = active work only (idle excluded by the Service).
        ProductiveTimeDisplay = Format(workTime);
        BreakSessionsDisplay = Math.Max(0, session.BreakSessionCount).ToString();

        if (session.ClockInAt is not null && session.ClockOutAt is not null)
        {
            var shift = session.ClockOutAt.Value - session.ClockInAt.Value;
            if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;
            TotalShiftDisplay = Format(shift);
            var onClock = shift - breakTime;
            WorkDurationDisplay = Format(onClock < workTime ? workTime : onClock);
        }
        else
        {
            TotalShiftDisplay = Format(workTime + breakTime + idle);
            WorkDurationDisplay = Format(workTime + idle);
        }

        StatusText = "Clocked Out";
        var sessionDay = session.ClockOutAt?.ToLocalTime() ?? session.ClockInAt?.ToLocalTime();
        _sessionDay = sessionDay?.Date;
        SessionEndedCaption = sessionDay is null
            ? "Your work session has ended."
            : $"Your work session for {sessionDay.Value:MMMM d, yyyy} has ended.";
        ApplyGreeting();

        RefreshTopAppsAndIdle();
    }

    private void ApplyGreeting()
    {
        var who = string.IsNullOrWhiteSpace(EmployeeName) ? string.Empty : $", {EmployeeName}";
        GreetingMessage = _activeSharePercent switch
        {
            null => $"Your workday is complete{who}.",
            >= 80 => $"Great job{who}! You've had a productive day. 🎉",
            >= 60 => $"Nice work{who}! Solid, steady day.",
            _ => $"Thanks{who}. Your workday is complete.",
        };
    }

    private void RefreshTopAppsAndIdle()
    {
        TopApps.Clear();
        var top = _dayMetrics.GetTopApps(4);
        if (top.Count == 0)
        {
            TopApps.Add(new TopAppItem("No app activity yet", "00:00:00"));
            return;
        }

        foreach (var (name, duration) in top)
            TopApps.Add(new TopAppItem(PrettyProcessName(name), Format(duration), _iconCache.GetIcon(name)));
    }

    /// <summary>Well-known process name -> display name. Anything not listed falls back to
    /// word-boundary splitting (e.g. "ApplicationFrameHost" -> "Application Frame Host").</summary>
    private static readonly Dictionary<string, string> FriendlyProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"]               = "Google Chrome",
        ["msedge"]               = "Microsoft Edge",
        ["firefox"]              = "Mozilla Firefox",
        ["code"]                 = "Visual Studio Code",
        ["devenv"]               = "Visual Studio",
        ["outlook"]              = "Microsoft Outlook",
        ["excel"]                = "Microsoft Excel",
        ["winword"]              = "Microsoft Word",
        ["powerpnt"]             = "Microsoft PowerPoint",
        ["onenote"]              = "Microsoft OneNote",
        ["onenotem"]             = "Microsoft OneNote",
        ["teams"]                = "Microsoft Teams",
        ["ms-teams"]             = "Microsoft Teams",
        ["slack"]                = "Slack",
        ["explorer"]             = "File Explorer",
        ["notepad"]              = "Notepad",
        ["applicationframehost"] = "Windows App",
        ["searchhost"]           = "Windows Search",
        ["textinputhost"]        = "Windows Input Experience",
        ["cmd"]                  = "Command Prompt",
        ["powershell"]           = "Windows PowerShell",
        ["windowsterminal"]      = "Windows Terminal",
    };

    internal static string PrettyProcessName(string processName)
    {
        var n = processName.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        if (n.Length == 0) return "Unknown";

        if (FriendlyProcessNames.TryGetValue(n, out var friendly))
            return friendly;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(SplitWordBoundaries(n).ToLowerInvariant());
    }

    /// <summary>Inserts a space before each uppercase letter that follows a lowercase one, so
    /// "ApplicationFrameHost" reads as "Application Frame Host" instead of one long word.</summary>
    private static string SplitWordBoundaries(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
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
            BreakSessionCount: breakTime > TimeSpan.Zero ? 1 : 0,
            AccumulatedIdle: afkTime));
        _ = meetingTime;
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    [RelayCommand]
    private async Task DownloadSummaryAsync()
    {
        try
        {
            var path = await DailySummaryPdfBuilder.WriteToDownloadsAsync(ToPdfData());
            Message = $"Summary saved to {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    internal DailySummaryPdfData ToPdfData() => new(
        StatusText, ClockInDisplay, ClockOutDisplay, TotalShiftDisplay,
        WorkingTimeDisplay, BreakTimeDisplay, IdleTimeDisplay,
        BreakSessionsDisplay, [.. TopApps],
        _dayMetrics.GetActivityChecks()
            .Select(s => new DailySummaryPdfScreenshot(
                s.CapturedAt.ToLocalTime().ToString("h:mm tt"),
                s.JpegBytes,
                s.IsSkipped))
            .ToList(),
        _sessionDay);

    [RelayCommand]
    private static void OpenDashboard()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = WorkspaceLinks.DashboardUrl,
                UseShellExecute = true
            });
        }
        catch { /* browser unavailable */ }
    }

    [RelayCommand]
    private static async Task ViewSummary()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.Summary); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private static async Task Back()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.ClockIn); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private static void CloseApp()
    {
        try
        {
            if (Application.Current?.Windows.Count is not > 0)
                return;
            var window = Application.Current.Windows[0];
#if WINDOWS
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
                && native.AppWindow is not null)
            {
                native.AppWindow.Hide();
                return;
            }
#endif
            _ = window;
        }
        catch { /* unit tests / window not ready */ }
    }
}
