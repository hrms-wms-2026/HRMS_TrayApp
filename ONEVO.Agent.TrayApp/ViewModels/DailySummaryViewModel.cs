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
    [ObservableProperty] private IReadOnlyList<float> _focusSparklinePoints = [];
    [ObservableProperty] private bool _hasTasks;
    [ObservableProperty] private bool _isLoadingTasks;
    [ObservableProperty] private string _tasksCaption = string.Empty;
    [ObservableProperty] private string _tasksEmptyMessage = "Loading today's tasks…";
    [ObservableProperty] private bool _canRetryTasks;

    // Work Time / Daily Goal card
    [ObservableProperty] private string _workedExclBreaksDisplay = "0m";
    [ObservableProperty] private bool _hasDailyGoal;
    [ObservableProperty] private string _goalPercentCaption = "—";
    [ObservableProperty] private string _goalTargetDisplay = string.Empty;
    [ObservableProperty] private string _goalCaption = "No work hours configured";
    [ObservableProperty] private double _goalFraction;
    [ObservableProperty] private IReadOnlyList<DonutSegment> _goalRingSegments = [new DonutSegment("#E5E7EB", 1f)];

    // Completed Tasks card
    [ObservableProperty] private string _tasksDoneDisplay = "—";
    [ObservableProperty] private string _tasksDoneCaption = "Loading today's tasks…";
    [ObservableProperty] private string _tasksPercentCaption = "—";
    [ObservableProperty] private int _tasksDoneCount;
    [ObservableProperty] private int _tasksPendingCount;
    [ObservableProperty] private int _tasksTotalCount;
    [ObservableProperty] private IReadOnlyList<DonutSegment> _taskRingSegments = [new DonutSegment("#E5E7EB", 1f)];

    // Work Timeline card
    [ObservableProperty] private IReadOnlyList<TimelineSegment> _timelineSegments = [];
    [ObservableProperty] private IReadOnlyList<TimelineTick> _timelineTicks = [];
    [ObservableProperty] private string? _timelineNote;
    [ObservableProperty] private string _breakTakenCaption = "(0m)";

    [ObservableProperty] private bool _hasAppUsage;
    [ObservableProperty] private string _workShareCaption = "No tracked activity yet";
    [ObservableProperty] private string _bannerMessage = "You stayed focused and made meaningful progress.";

    public ObservableCollection<TopAppItem> TopApps { get; } = [];
    public ObservableCollection<DailyScreenshotItem> Screenshots { get; } = [];
    public ObservableCollection<DailyTaskItem> Tasks { get; } = [];
    /// <summary>Latest activity checks for the 3×2 summary grid (the PDF still gets all of them).</summary>
    public ObservableCollection<DailyScreenshotItem> ScreenshotTiles { get; } = [];
    /// <summary>App Used Time rows: top apps by share of all tracked app time, plus "Others".</summary>
    public ObservableCollection<AppUsageRow> AppUsageRows { get; } = [];

    internal const int ScreenshotTileCount = 6;
    private const int AppRowCount = 5;
    internal const string ActiveColor = "#3B82F6";
    internal const string BreakColor = "#F59E0B";
    internal const string IdleColor = "#C4B5FD";

    private static readonly string[] AppPalette = ["#3B82F6", "#6366F1", "#22C55E", "#F59E0B", "#94A3B8"];

    /// <summary>Footer IPC connection state (shared singleton in the app).</summary>
    public ServiceConnectionStatus Connection { get; }

    public DailySummaryViewModel(INamedPipeClient pipe, ISessionDayMetrics dayMetrics, IAppIconCache iconCache,
        ServiceConnectionStatus? connection = null)
    {
        Title = "Daily Summary";
        Connection = connection ?? new ServiceConnectionStatus();
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

        var source = new EndSessionViewModel(_pipe, _dayMetrics, _iconCache, Connection);
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
        LoadAppUsage();

        LoadScreenshots();
        FocusSparklinePoints = _dayMetrics.GetHourlyFocusFractions().Select(f => (float)f).ToList();
        WorkedExclBreaksDisplay = Compact(Parse(source.WorkDurationDisplay));

        ApplyDerived();
        ApplySessionVisuals(session);
        _ = LoadTasksAsync(CancellationToken.None);
    }

    /// <summary>
    /// Pulls today's task clock-ins from the backend through the Service. Fails soft: the rest of
    /// the summary is local data and must still render when the backend or pipe is unavailable.
    /// </summary>
    public async Task LoadTasksAsync(CancellationToken ct)
    {
        if (IsLoadingTasks)
            return;

        IsLoadingTasks = true;
        CanRetryTasks = false;
        TasksEmptyMessage = "Loading today's tasks…";
        try
        {
            DailyTasksResultPayload? result;
            try
            {
                result = await _pipe.SendDailyTasksRequestAsync(ct);
            }
            catch
            {
                result = null;
            }

            Tasks.Clear();
            if (result is null || !result.Success)
            {
                HasTasks = false;
                TasksCaption = string.Empty;
                TasksEmptyMessage = "Task details are unavailable right now.";
                CanRetryTasks = true;
                ApplyTaskTotals(null);
                return;
            }

            // Schedule order: by first clock-in when the Service sends it (older builds don't).
            IEnumerable<DailyTaskItemPayload> ordered = result.Tasks.All(t => t.FirstClockInAtUtc is not null)
                ? result.Tasks.OrderBy(t => t.FirstClockInAtUtc)
                : result.Tasks;
            foreach (var task in ordered)
                Tasks.Add(DailyTaskItem.From(task));
            ApplyTaskTotals(result);

            HasTasks = Tasks.Count > 0;
            TasksCaption = HasTasks
                ? $"{result.DoneCount} of {result.TaskCount} done · {Compact(TimeSpan.FromMinutes(result.TotalMinutes))}"
                : string.Empty;
            TasksEmptyMessage = HasTasks
                ? string.Empty
                : "No task clock-ins today. Clock in to a task in OneXso to track it here.";
        }
        finally
        {
            IsLoadingTasks = false;
        }
    }

    private void ApplyTaskTotals(DailyTasksResultPayload? result)
    {
        if (result is null)
        {
            TasksDoneDisplay = "—";
            TasksDoneCaption = "Task details are unavailable right now.";
            TasksPercentCaption = "—";
            TasksDoneCount = TasksPendingCount = TasksTotalCount = 0;
            TaskRingSegments = [new DonutSegment("#E5E7EB", 1f)];
            ApplyBanner();
            return;
        }

        var total = Math.Max(0, result.TaskCount);
        var done = Math.Clamp(result.DoneCount, 0, total);
        TasksTotalCount = total;
        TasksDoneCount = done;
        TasksPendingCount = total - done;
        TasksDoneDisplay = $"{done} task{(done == 1 ? "" : "s")}";
        TasksDoneCaption = total == 0
            ? "No task clock-ins today"
            : $"{done} of {total} done · {Compact(TimeSpan.FromMinutes(result.TotalMinutes))} on tasks";
        var pct = total == 0 ? 0 : (int)Math.Round(100.0 * done / total);
        TasksPercentCaption = total == 0 ? "—" : $"{pct}%";
        TaskRingSegments = Ring(total == 0 ? 0 : (double)done / total, "#22C55E");
        ApplyBanner();
    }

    private void ApplyBanner()
    {
        var focus = HighlightProgress.TrimEnd('.');
        BannerMessage = TasksDoneCount > 0
            ? $"{focus} and completed {TasksDoneCount} task{(TasksDoneCount == 1 ? "" : "s")} today."
            : $"{focus}.";
    }

    /// <summary>Goal ring, timeline and break caption: everything that needs the raw session.</summary>
    private void ApplySessionVisuals(SessionSnapshot? session)
    {
        var work = Parse(WorkingTimeDisplay);
        var brk = Parse(BreakTimeDisplay);
        var policy = _pipe.LastKnownPolicy;
        var goal = DailyGoal(policy?.ScheduleStart, policy?.ScheduleEnd, session?.BreakAllowanceMinutes);
        HasDailyGoal = goal is not null;
        if (goal is { } target)
        {
            var fraction = work.TotalSeconds / target.TotalSeconds;
            var pct = (int)Math.Round(100 * fraction);
            GoalFraction = Math.Clamp(fraction, 0, 1);
            GoalPercentCaption = $"{pct}%";
            GoalTargetDisplay = $"/ {Compact(target)}";
            GoalCaption = $"{pct}% of daily goal";
            GoalRingSegments = Ring(GoalFraction, "#6366F1");
        }
        else
        {
            GoalFraction = 0;
            GoalPercentCaption = "—";
            GoalTargetDisplay = string.Empty;
            GoalCaption = "No work hours configured";
            GoalRingSegments = [new DonutSegment("#E5E7EB", 1f)];
        }

        BreakTakenCaption = $"({Compact(brk)})";

        if (session?.ClockInAt is { } clockIn)
        {
            var end = session.ClockOutAt ?? DateTimeOffset.Now;
            var (segments, ticks) = BuildTimeline(clockIn, end, session.Pauses);
            TimelineSegments = segments;
            TimelineTicks = ticks;
            var paused = session.AccumulatedBreak + session.AccumulatedIdle;
            TimelineNote = session.Pauses is null && paused > TimeSpan.Zero
                ? "Break and idle times for this day aren't placed on the timeline."
                : null;
        }
        else
        {
            TimelineSegments = [];
            TimelineTicks = [];
            TimelineNote = "No clock-in recorded for this day.";
        }

        ApplyBanner();
    }

    private static IReadOnlyList<DonutSegment> Ring(double fraction, string color)
    {
        var f = (float)Math.Clamp(fraction, 0, 1);
        if (f >= 0.999f) return [new DonutSegment(color, 1f)];
        if (f <= 0.001f) return [new DonutSegment("#E5E7EB", 1f)];
        return [new DonutSegment(color, f), new DonutSegment("#E5E7EB", 1f - f)];
    }

    /// <summary>
    /// Daily target from the legal entity's default work hours, less the break allowance (work
    /// time excludes breaks). Null when no schedule is configured — never a guessed 8h.
    /// </summary>
    internal static TimeSpan? DailyGoal(TimeOnly? start, TimeOnly? end, int? breakAllowanceMinutes)
    {
        if (start is not { } s || end is not { } e || s == e)
            return null;

        var span = e - s; // wraps past midnight for overnight shifts
        if (breakAllowanceMinutes is > 0 and var allowance && TimeSpan.FromMinutes(allowance) < span)
            span -= TimeSpan.FromMinutes(allowance);
        return span > TimeSpan.Zero ? span : null;
    }

    /// <summary>
    /// Active stretch from clock-in to clock-out with break/idle stretches over it, on a range
    /// rounded out to whole hours. <paramref name="pauses"/> null = only the active span is known.
    /// </summary>
    internal static (IReadOnlyList<TimelineSegment> Segments, IReadOnlyList<TimelineTick> Ticks) BuildTimeline(
        DateTimeOffset clockIn,
        DateTimeOffset end,
        IReadOnlyList<SessionPauseInterval>? pauses)
    {
        var inLocal = clockIn.ToLocalTime();
        var endLocal = end.ToLocalTime();
        if (endLocal <= inLocal)
            endLocal = inLocal.AddMinutes(1);

        var rangeStart = new DateTimeOffset(inLocal.Year, inLocal.Month, inLocal.Day, inLocal.Hour, 0, 0, inLocal.Offset);
        var rangeEnd = new DateTimeOffset(endLocal.Year, endLocal.Month, endLocal.Day, endLocal.Hour, 0, 0, endLocal.Offset);
        if (rangeEnd < endLocal)
            rangeEnd = rangeEnd.AddHours(1);
        if (rangeEnd - rangeStart < TimeSpan.FromHours(1))
            rangeEnd = rangeStart.AddHours(1);

        var total = (rangeEnd - rangeStart).TotalSeconds;
        double Pos(DateTimeOffset t) => Math.Clamp((t - rangeStart).TotalSeconds / total, 0, 1);

        var segments = new List<TimelineSegment> { new(Pos(inLocal), Pos(endLocal), ActiveColor) };
        foreach (var pause in pauses ?? [])
        {
            var from = pause.StartedAt < clockIn ? clockIn : pause.StartedAt;
            var to = pause.EndedAt > end ? end : pause.EndedAt;
            if (to <= from)
                continue;
            var color = pause.Kind == SessionPauseInterval.Break ? BreakColor : IdleColor;
            segments.Add(new TimelineSegment(Pos(from), Pos(to), color));
        }

        var hours = (int)Math.Round((rangeEnd - rangeStart).TotalHours);
        var step = hours <= 10 ? 1 : hours <= 20 ? 2 : 4;
        var ticks = new List<TimelineTick>();
        for (var h = 0; h <= hours; h += step)
        {
            var t = rangeStart.AddHours(h);
            ticks.Add(new TimelineTick(Pos(t), t.ToString("HH:mm")));
        }

        return (segments, ticks);
    }

    private void LoadAppUsage()
    {
        AppUsageRows.Clear();
        var all = _dayMetrics.GetTopApps(int.MaxValue);
        var total = all.Aggregate(TimeSpan.Zero, (sum, app) => sum + app.Duration);
        HasAppUsage = total > TimeSpan.Zero;
        if (!HasAppUsage)
            return;

        var top = all.Take(AppRowCount).ToList();
        var maxShare = top[0].Duration.TotalSeconds / total.TotalSeconds;
        for (var i = 0; i < top.Count; i++)
        {
            var (name, duration) = top[i];
            AppUsageRows.Add(AppUsageRow.Create(
                EndSessionViewModel.PrettyProcessName(name), duration, total, maxShare,
                AppPalette[i % AppPalette.Length], _iconCache.GetIcon(name)));
        }

        var others = all.Skip(AppRowCount).Aggregate(TimeSpan.Zero, (sum, app) => sum + app.Duration);
        if (others > TimeSpan.Zero)
            AppUsageRows.Add(AppUsageRow.Create("Others", others, total, maxShare, "#94A3B8", null));
    }

    public void LoadFromSnapshot(SessionSnapshot session)
    {
        var source = new EndSessionViewModel(_pipe, _dayMetrics, _iconCache, Connection);
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
        LoadAppUsage();
        LoadScreenshots();
        FocusSparklinePoints = _dayMetrics.GetHourlyFocusFractions().Select(f => (float)f).ToList();
        WorkedExclBreaksDisplay = Compact(Parse(source.WorkDurationDisplay));
        ApplyDerived();
        ApplySessionVisuals(session);
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
        ScreenshotTiles.Clear();
        foreach (var tile in Screenshots.Skip(Math.Max(0, Screenshots.Count - ScreenshotTileCount)))
            ScreenshotTiles.Add(tile);
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

    /// <summary>Praise only what the numbers support: verdict tiers come from the active share.</summary>
    private void ApplyDayVerdict(TimeSpan tracked, int activeShare)
    {
        var who = string.IsNullOrWhiteSpace(EmployeeName) ? string.Empty : $", {EmployeeName}";
        if (tracked.TotalMinutes < 1)
        {
            Headline = "Here's how your day went.";
            ExcellentDayCaption = "No tracked activity yet";
        }
        else if (activeShare >= 80)
        {
            Headline = "Here's how your day went. Keep up the excellent work!";
            ExcellentDayCaption = $"Excellent day{who}!";
        }
        else if (activeShare >= 60)
        {
            Headline = "Here's how your day went. Nice, steady work.";
            ExcellentDayCaption = $"Good day{who}!";
        }
        else
        {
            Headline = "Here's how your day went. Tomorrow is a fresh start.";
            ExcellentDayCaption = $"Day complete{who}";
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
        ApplyDayVerdict(tracked, activeShare);
        ActiveShareCaption = $"{activeShare}%";
        IdleShareCaption = $"{100 - activeShare}%";
        ActiveShareFraction = activeShare / 100.0;
        WorkShareCaption = tracked.TotalSeconds <= 0
            ? "No tracked activity yet"
            : $"Active {activeShare}% of tracked time";
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
        // Peak focus hour from tracked activity — not the whole clock-in/out window.
        HighlightWindow = _dayMetrics.GetPeakFocusHour() is int hour
            ? $"{DateTime.Today.AddHours(hour):hh:mm tt} – {DateTime.Today.AddHours(hour + 1):hh:mm tt}"
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

    internal static string Compact(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.Minutes > 0)
            return $"{t.Minutes}m";
        return $"{Math.Max(0, t.Seconds)}s";
    }

    [RelayCommand]
    private Task RetryTasksAsync() => LoadTasksAsync(CancellationToken.None);

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

public sealed record DailyTaskItem(
    string Title,
    string Subtitle,
    string StatusName,
    string StatusColorHex,
    string TimeDisplay,
    double ProgressFraction,
    string ProgressCaption,
    bool IsRunning,
    bool IsDone,
    string TimeRange = "")
{
    public bool HasTimeRange => !string.IsNullOrEmpty(TimeRange);
    /// <summary>Schedule column: the clock-in range, or just today's time on older Service builds.</summary>
    public string ScheduleTime => HasTimeRange ? TimeRange : TimeDisplay;

    private const string DefaultStatusColor = "#94A3B8";

    public Color StatusSwatch =>
        Color.TryParse(StatusColorHex, out var color) ? color : Color.FromArgb(DefaultStatusColor);

    public static DailyTaskItem From(DailyTaskItemPayload task)
    {
        var subtitle = string.IsNullOrWhiteSpace(task.ProjectName)
            ? task.ShortId
            : string.IsNullOrWhiteSpace(task.ShortId) ? task.ProjectName : $"{task.ShortId} · {task.ProjectName}";
        var progress = Math.Clamp(task.ProgressPercent, 0, 100);

        return new DailyTaskItem(
            Title: string.IsNullOrWhiteSpace(task.Title) ? "Untitled task" : task.Title,
            Subtitle: subtitle,
            StatusName: string.IsNullOrWhiteSpace(task.StatusName) ? (task.IsDone ? "Done" : "In progress") : task.StatusName,
            StatusColorHex: string.IsNullOrWhiteSpace(task.StatusColor) ? DefaultStatusColor : task.StatusColor,
            TimeDisplay: DailySummaryViewModel.Compact(TimeSpan.FromMinutes(Math.Max(0, task.MinutesToday))),
            ProgressFraction: progress / 100.0,
            ProgressCaption: task is { TodayFromPercent: int from, TodayToPercent: int to }
                ? $"{from}% → {to}%"
                : $"{progress}%",
            IsRunning: task.IsClockedInNow,
            IsDone: task.IsDone,
            TimeRange: task.FirstClockInAtUtc is { } first
                ? $"{first.ToLocalTime():HH:mm} - {(task.IsClockedInNow ? "now" : (task.LastActivityAtUtc ?? first).ToLocalTime().ToString("HH:mm"))}"
                : string.Empty);
    }
}

/// <summary>One App Used Time row. <see cref="BarFraction"/> is relative to the top app so the
/// longest bar fills its track; <see cref="Percent"/> is the share of all tracked app time.</summary>
public sealed record AppUsageRow(
    string Name,
    string DurationText,
    string Percent,
    double BarFraction,
    string ColorHex,
    ImageSource? Icon)
{
    public Color Swatch => Color.FromArgb(ColorHex);
    public bool HasIcon => Icon is not null;

    internal static AppUsageRow Create(
        string name, TimeSpan duration, TimeSpan total, double maxShare, string colorHex, ImageSource? icon)
    {
        var share = duration.TotalSeconds / total.TotalSeconds;
        return new AppUsageRow(
            name,
            DailySummaryViewModel.Compact(duration),
            $"{(int)Math.Round(100 * share)}%",
            maxShare <= 0 ? 0 : Math.Clamp(share / maxShare, 0, 1),
            colorHex,
            icon);
    }
}
