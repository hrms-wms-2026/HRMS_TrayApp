namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// In-memory day metrics for the Tray end-of-day summary (local session).
/// Collectors and lifecycle results publish here; EndSession reads on appear.
/// </summary>
public interface ISessionDayMetrics
{
    SessionSnapshot? LastCompletedSession { get; }

    void RememberCompletedSession(SessionSnapshot session);

    void AddAppUsageSample(string processName, TimeSpan sampleWindow);

    void AddIdleSample(TimeSpan idlePortion);

    void ResetDay();

    IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5);

    TimeSpan TotalIdle { get; }
}
