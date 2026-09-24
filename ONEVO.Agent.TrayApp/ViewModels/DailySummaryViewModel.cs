namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Collections.ObjectModel;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Controls;
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
    [ObservableProperty] private double _idleShareFraction;
    [ObservableProperty] private string _focusTrendCaption = "Focused hours";
    [ObservableProperty] private bool _isFocusTrendPositive;
    [ObservableProperty] private string _insightFocus = "Stay focused — every hour counts.";
    [ObservableProperty] private string _insightIdle = "Idle time is tracked so you can improve tomorrow.";
    [ObservableProperty] private string _insightBreaks = "Regular breaks help you recharge.";
    [ObservableProperty] private string _highlightProgress = "You stayed focused and made meaningful progress.";
    [ObservableProperty] private string _highlightFocus = "Keep building consistent focus time.";
    [ObservableProperty] private string _highlightWindow = "";
    [ObservableProperty] private string _highlightProgressTitle = "Progress";
    [ObservableProperty] private string _mostProductiveCaption = "Most Productive Period";
    [ObservableProperty] private string _breakSessionsCaption = "0 sessions";
    [ObservableProperty] private bool _hasBreaks;
    [ObservableProperty] private bool _hasScreenshots;
    [ObservableProperty] private string _screenshotsCaption = "No activity-check screenshots today.";
    [ObservableProperty] private IReadOnlyList<DonutSegment> _appDonutSegments = [];

    public ObservableCollection<TopAppItem> TopApps { get; } = [];
    public ObservableCollection<DailyScreenshotItem> Screenshots { get; } = [];

    private static readonly string[] AppPalette = ["#3B82F6", "#6366F1", "#22C55E", "#F59E0B", "#94A3B8"];

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

        LoadScreenshots();

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
        LoadScreenshots();
        ApplyDerived();
    }

    private void LoadScreenshots()
    {
        Screenshots.Clear();
        foreach (var shot in _dayMetrics.GetActivityChecks())
        {
            var jpeg = shot.JpegBytes;
            Screenshots.Add(new DailyScreenshotItem(
                shot.CapturedAt.ToLocalTime().ToString("h:mm tt"),
                jpeg,
                shot.IsSkipped ? null : TryPreview(jpeg),
                shot.IsSkipped));
        }

        HasScreenshots = Screenshots.Count > 0;
        ScreenshotsCaption = BuildScreenshotsCaption(Screenshots);
    }

    private static string BuildScreenshotsCaption(IReadOnlyList<DailyScreenshotItem> items)
    {
        var allowed = 0;
        var skipped = 0;
        foreach (var item in items)
        {
            if (item.IsSkipped) skipped++;
            else allowed++;
        }

        if (allowed == 0 && skipped == 0)
            return "No activity-check screenshots today. Allow on the prompt to capture one.";
        if (skipped == 0)
            return $"{allowed} screenshot{(allowed == 1 ? "" : "s")} you allowed during activity checks.";
        if (allowed == 0)
            return $"{skipped} skipped activity check{(skipped == 1 ? "" : "s")}.";
        return $"{allowed} screenshot{(allowed == 1 ? "" : "s")} allowed, {skipped} skipped.";
    }

    private static ImageSource? TryPreview(byte[] jpeg)
    {
        try
        {
            return ImageSource.FromStream(() => new MemoryStream(jpeg));
        }
        catch
        {
            return null;
        }
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
        IdleShareFraction = (100 - activeShare) / 100.0;
        if (tracked.TotalSeconds <= 0)
        {
            FocusTrendCaption = "Focused hours";
            IsFocusTrendPositive = false;
        }
        else if (activeShare >= 50)
        {
            FocusTrendCaption = $"↑ {activeShare}% tracked";
            IsFocusTrendPositive = true;
        }
        else
        {
            FocusTrendCaption = $"{activeShare}% tracked";
            IsFocusTrendPositive = false;
        }
        BreakSessionsCaption = int.TryParse(BreakSessionsDisplay, out var n)
            ? $"{n} break{(n == 1 ? "" : "s")}"
            : BreakSessionsDisplay;

        InsightFocus = work.TotalMinutes < 1
            ? "Stay focused — every hour counts."
            : activeShare >= 80
                ? $"You were highly focused for {Compact(work)} today."
                : $"You were focused for {Compact(work)} today.";
        InsightIdle = idle.TotalMinutes >= 1
            ? $"Idle time was {Compact(idle)}. A short stretch can help you reset."
            : "Very little idle time — great concentration.";
        InsightBreaks = n > 0
            ? "Great job taking regular breaks."
            : "A short break can help you recharge tomorrow.";

        HasBreaks = n > 0;
        HighlightProgressTitle = activeShare >= 80 ? "Great Progress" : "Keep going";
        HighlightProgress = $"You stayed focused {activeShare}% of tracked time.";
        HighlightFocus = $"Focus time {Compact(focus)}.";
        HighlightWindow = ClockInDisplay != "—" && ClockOutDisplay != "—"
            ? $"{ClockInDisplay} – {ClockOutDisplay}"
            : string.Empty;
        MostProductiveCaption = string.IsNullOrEmpty(HighlightWindow)
            ? "Most Productive Period"
            : $"Most Productive Period  {HighlightWindow}";

        var totalApp = TimeSpan.Zero;
        foreach (var app in TopApps)
        {
            if (TimeSpan.TryParse(app.Duration, out var d))
                totalApp += d;
        }

        if (totalApp > TimeSpan.Zero)
        {
            var withShare = TopApps.Select((app, i) =>
            {
                var dur = TimeSpan.TryParse(app.Duration, out var d) ? d : TimeSpan.Zero;
                var pct = (int)Math.Round(100.0 * dur.TotalSeconds / totalApp.TotalSeconds);
                return app with
                {
                    Percent = $"{pct}%",
                    ColorHex = AppPalette[i % AppPalette.Length],
                    Fraction = dur.TotalSeconds / totalApp.TotalSeconds,
                    DisplayDuration = Compact(dur),
                };
            }).ToList();
            TopApps.Clear();
            foreach (var app in withShare)
                TopApps.Add(app);
            AppDonutSegments = withShare
                .Select(app => new DonutSegment(app.ColorHex, (float)app.Fraction))
                .ToArray();
        }
        else
        {
            AppDonutSegments = [new DonutSegment("#E5E7EB", 1f)];
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
                BreakSessionsDisplay, [.. TopApps],
                Screenshots.Select(s => new DailySummaryPdfScreenshot(s.TimeDisplay, s.JpegBytes, s.IsSkipped)).ToList()));
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

public sealed record DailyScreenshotItem(
    string TimeDisplay,
    byte[] JpegBytes,
    ImageSource? Preview,
    bool IsSkipped = false)
{
    public string NoteText => IsSkipped ? "Screenshot skipped" : string.Empty;
}
