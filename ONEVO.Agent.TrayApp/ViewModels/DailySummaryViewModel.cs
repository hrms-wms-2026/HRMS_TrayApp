namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Collections.ObjectModel;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class DailySummaryViewModel : BaseViewModel
{
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly IPreferencesStore _preferences;

    [ObservableProperty] private string _employeeName = "there";
    [ObservableProperty] private string _dateDisplay = "";
    [ObservableProperty] private string _activeTimeDisplay = "00:00";
    [ObservableProperty] private string _idleTimeDisplay = "00:00";
    [ObservableProperty] private string _breakTimeDisplay = "00:00";
    [ObservableProperty] private string _productiveTimeDisplay = "00:00";
    [ObservableProperty] private string _breakSessionsDisplay = "0";
    [ObservableProperty] private string _greetingLine = "";
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<TopAppItem> TopApps { get; } = [];

    public DailySummaryViewModel(ISessionDayMetrics dayMetrics, IPreferencesStore preferences)
    {
        Title = "Daily Summary";
        _dayMetrics = dayMetrics;
        _preferences = preferences;
    }

    public void OnAppearing()
    {
        EmployeeName = EmployeeSession.Name(_preferences, "there");
        if (EmployeeName is "—" or "") EmployeeName = "there";
        DateDisplay = DateTime.Now.ToString("MMMM d, yyyy  •  dddd");
        GreetingLine = $"Excellent day, {EmployeeName}!";

        var session = _dayMetrics.LastCompletedSession;
        if (session is not null)
            LoadFromSnapshot(session);

        TopApps.Clear();
        foreach (var (name, duration) in _dayMetrics.GetTopApps(5))
            TopApps.Add(new TopAppItem(name, Format(duration)));
        if (TopApps.Count == 0)
            TopApps.Add(new TopAppItem("No app activity yet", "00:00"));
    }

    private void LoadFromSnapshot(SessionSnapshot session)
    {
        var breakTime = session.AccumulatedBreak < TimeSpan.Zero ? TimeSpan.Zero : session.AccumulatedBreak;
        var idle = session.AccumulatedIdle < TimeSpan.Zero ? TimeSpan.Zero : session.AccumulatedIdle;
        var work = session.AccumulatedWork < TimeSpan.Zero ? TimeSpan.Zero : session.AccumulatedWork;
        if (work == TimeSpan.Zero && session.ClockInAt is not null && session.ClockOutAt is not null)
        {
            var wall = session.ClockOutAt.Value - session.ClockInAt.Value;
            if (wall < TimeSpan.Zero) wall = TimeSpan.Zero;
            work = wall - breakTime - idle;
            if (work < TimeSpan.Zero) work = TimeSpan.Zero;
        }

        ActiveTimeDisplay = FormatShort(work);
        IdleTimeDisplay = FormatShort(idle);
        BreakTimeDisplay = FormatShort(breakTime);
        ProductiveTimeDisplay = FormatShort(work);
        BreakSessionsDisplay = Math.Max(0, session.BreakSessionCount).ToString();
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private static string FormatShort(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m";

    [RelayCommand]
    private async Task DownloadSummaryAsync()
    {
        try
        {
            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dir = Path.Combine(downloads, "Downloads");
            if (!Directory.Exists(dir)) dir = downloads;
            var path = Path.Combine(dir, $"OneXso-Daily-Summary-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            var bytes = DailySummaryPdfBuilder.Build(new DailySummaryPdfData(
                "Clocked Out", "—", "—", ActiveTimeDisplay,
                ActiveTimeDisplay, BreakTimeDisplay, ProductiveTimeDisplay, IdleTimeDisplay,
                BreakSessionsDisplay, [.. TopApps]));
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private static void OpenInsights()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = WorkspaceLinks.DashboardUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private static async Task Done()
    {
        try { await Shell.Current.GoToAsync("//clockin"); }
        catch { /* unit tests */ }
    }
}
