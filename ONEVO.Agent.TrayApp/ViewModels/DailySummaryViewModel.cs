namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Collections.ObjectModel;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class DailySummaryViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly IAppIconCache _iconCache;

    [ObservableProperty] private string _employeeName = string.Empty;
    [ObservableProperty] private string _dateDisplay = DateTime.Now.ToString("MMMM d, yyyy");
    [ObservableProperty] private string _weekdayDisplay = DateTime.Now.ToString("dddd");
    [ObservableProperty] private string _workingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _idleTimeDisplay = "00:00:00";
    [ObservableProperty] private string _breakTimeDisplay = "00:00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00:00";
    [ObservableProperty] private string _totalShiftDisplay = "00:00:00";
    [ObservableProperty] private string _clockInDisplay = "—";
    [ObservableProperty] private string _clockOutDisplay = "—";
    [ObservableProperty] private string _breakSessionsDisplay = "0";
    [ObservableProperty] private string _statusText = "Clocked Out";
    [ObservableProperty] private string _headline = "Here's how your day went. Keep up the excellent work!";
    [ObservableProperty] private string _excellentDayCaption = "Excellent day!";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _focusCompactDisplay = "0m";
    [ObservableProperty] private string _activeCompactDisplay = "0m";
    [ObservableProperty] private string _idleCompactDisplay = "0m";
    [ObservableProperty] private string _breakCompactDisplay = "0m";
    [ObservableProperty] private string _activeShareCaption = "0%";
    [ObservableProperty] private string _idleShareCaption = "0%";
    [ObservableProperty] private double _activeShareFraction = 1;
    [ObservableProperty] private string _insightFocus = "Stay focused — every hour counts.";
    [ObservableProperty] private string _insightIdle = "Idle time is tracked so you can improve tomorrow.";
    [ObservableProperty] private string _insightBreaks = "Regular breaks help you recharge.";
    [ObservableProperty] private string _highlightProgress = "You stayed focused and made meaningful progress.";
    [ObservableProperty] private string _highlightFocus = "Keep building consistent focus time.";
    [ObservableProperty] private string _highlightWindow = "";
    [ObservableProperty] private string _breakSessionsCaption = "0 sessions";

    public ObservableCollection<TopAppItem> TopApps { get; } = [];

    public DailySummaryViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics, IAppIconCache iconCache)
    {
        Title = "Daily Summary";
        _pipe = pipe;
        _dayMetrics = dayMetrics;
        _iconCache = iconCache;
    }

    public DailySummaryViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics)
        : this(pipe, dayMetrics, NullAppIconCache.Instance) { }

    public void OnAppearing()
    {
        try
        {
            EmployeeName = Microsoft.Maui.Storage.Preferences.Get("onevo.employee_display_name", string.Empty);
        }
        catch { /* unit tests */ }

        var source = new EndSessionViewModel(_pipe, _dayMetrics, _iconCache);
        if (_dayMetrics.LastCompletedSession is { } completed)
            source.LoadFromSnapshot(completed);
        else if (_pipe.LastKnownStatus?.Session is { } cached)
            source.LoadFromSnapshot(cached);

        ClockInDisplay = source.ClockInDisplay;
        ClockOutDisplay = source.ClockOutDisplay;
        TotalShiftDisplay = source.TotalShiftDisplay;
        WorkingTimeDisplay = source.WorkingTimeDisplay;
        BreakTimeDisplay = source.BreakTimeDisplay;
        ProductiveTimeDisplay = source.ProductiveTimeDisplay;
        IdleTimeDisplay = source.IdleTimeDisplay;
        BreakSessionsDisplay = source.BreakSessionsDisplay;
        StatusText = source.StatusText;

        var session = _dayMetrics.LastCompletedSession ?? _pipe.LastKnownStatus?.Session;
        var sessionDay = session?.ClockOutAt?.ToLocalTime() ?? session?.ClockInAt?.ToLocalTime();
        if (sessionDay is not null)
        {
            DateDisplay = sessionDay.Value.ToString("MMMM d, yyyy");
            WeekdayDisplay = sessionDay.Value.ToString("dddd");
        }

        TopApps.Clear();
        foreach (var app in source.TopApps)
            TopApps.Add(app);

        Headline = "Here's how your day went. Keep up the excellent work!";
        ExcellentDayCaption = string.IsNullOrWhiteSpace(EmployeeName)
            ? "Excellent day!"
            : $"Excellent day, {EmployeeName}!";
        ApplyDerived();
    }

    public void LoadFromSnapshot(SessionSnapshot session)
    {
        var source = new EndSessionViewModel(_pipe, _dayMetrics, _iconCache);
        source.LoadFromSnapshot(session);
        ClockInDisplay = source.ClockInDisplay;
        ClockOutDisplay = source.ClockOutDisplay;
        TotalShiftDisplay = source.TotalShiftDisplay;
        WorkingTimeDisplay = source.WorkingTimeDisplay;
        BreakTimeDisplay = source.BreakTimeDisplay;
        ProductiveTimeDisplay = source.ProductiveTimeDisplay;
        IdleTimeDisplay = source.IdleTimeDisplay;
        BreakSessionsDisplay = source.BreakSessionsDisplay;
        StatusText = source.StatusText;
        TopApps.Clear();
        foreach (var app in source.TopApps)
            TopApps.Add(app);
        ApplyDerived();
    }

    private void ApplyDerived()
    {
        var work = Parse(WorkingTimeDisplay);
        var idle = Parse(IdleTimeDisplay);
        var brk = Parse(BreakTimeDisplay);
        var focus = Parse(ProductiveTimeDisplay);
        if (focus <= TimeSpan.Zero)
            focus = work;

        FocusCompactDisplay = Compact(focus);
        ActiveCompactDisplay = Compact(work);
        IdleCompactDisplay = Compact(idle);
        BreakCompactDisplay = Compact(brk);

        var tracked = work + idle;
        var activeShare = tracked.TotalSeconds <= 0 ? 100 : (int)Math.Round(100.0 * work.TotalSeconds / tracked.TotalSeconds);
        activeShare = Math.Clamp(activeShare, 0, 100);
        ActiveShareCaption = $"{activeShare}%";
        IdleShareCaption = $"{100 - activeShare}%";
        ActiveShareFraction = activeShare / 100.0;
        BreakSessionsCaption = int.TryParse(BreakSessionsDisplay, out var n)
            ? $"{n} break{(n == 1 ? "" : "s")}"
            : BreakSessionsDisplay;

        InsightFocus = work.TotalMinutes >= 1
            ? $"You were focused for {Compact(work)} today."
            : "Stay focused — every hour counts.";
        InsightIdle = idle.TotalMinutes >= 1
            ? $"Idle time was {Compact(idle)}. A short stretch can help you reset."
            : "Very little idle time — great concentration.";
        InsightBreaks = n > 0
            ? "Great job taking regular breaks."
            : "A short break can help you recharge tomorrow.";

        HighlightProgress = activeShare >= 80
            ? $"Great Progress. You stayed focused {activeShare}% of tracked time."
            : $"You stayed focused {activeShare}% of tracked time.";
        HighlightFocus = $"Focus time {Compact(focus)}.";
        HighlightWindow = ClockInDisplay != "—" && ClockOutDisplay != "—"
            ? $"Most Productive  {ClockInDisplay} – {ClockOutDisplay}"
            : string.Empty;

        var totalApp = TimeSpan.Zero;
        foreach (var app in TopApps)
        {
            if (TimeSpan.TryParse(app.Duration, out var d))
                totalApp += d;
        }

        if (totalApp > TimeSpan.Zero)
        {
            var withShare = TopApps.Select(app =>
            {
                var dur = TimeSpan.TryParse(app.Duration, out var d) ? d : TimeSpan.Zero;
                var pct = (int)Math.Round(100.0 * dur.TotalSeconds / totalApp.TotalSeconds);
                return app with { Percent = $"{pct}%" };
            }).ToList();
            TopApps.Clear();
            foreach (var app in withShare)
                TopApps.Add(app);
        }
    }

    private static TimeSpan Parse(string value) =>
        TimeSpan.TryParse(value, out var t) ? t : TimeSpan.Zero;

    private static string Compact(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.Minutes > 0)
            return $"{t.Minutes}m";
        return $"{Math.Max(0, t.Seconds)}s";
    }

    [RelayCommand]
    private async Task DownloadSummaryAsync()
    {
        try
        {
            var path = await DailySummaryPdfBuilder.WriteToDownloadsAsync(new DailySummaryPdfData(
                StatusText, ClockInDisplay, ClockOutDisplay, TotalShiftDisplay,
                WorkingTimeDisplay, BreakTimeDisplay, ProductiveTimeDisplay, IdleTimeDisplay,
                BreakSessionsDisplay, [.. TopApps]));
            Message = $"Summary saved to {path}";
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private static async Task Back()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.End); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private static async Task Done()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.ClockIn); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private static void ViewInsights()
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
}
