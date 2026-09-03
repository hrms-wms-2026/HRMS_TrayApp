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
}
