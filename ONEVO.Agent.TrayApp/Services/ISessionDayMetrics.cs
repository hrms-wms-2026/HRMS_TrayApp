namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// One activity-check screenshot the employee allowed this session. Bytes stay in
/// process memory for Daily Summary / PDF only — they are not written to logs.
/// </summary>
public sealed record SessionScreenshot(Guid AttemptId, DateTimeOffset CapturedAt, byte[] JpegBytes);

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

    void AddAllowedScreenshot(Guid attemptId, DateTimeOffset capturedAt, ReadOnlyMemory<byte> jpegBytes);

    void ResetDay();

    IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5);

    IReadOnlyList<SessionScreenshot> GetAllowedScreenshots();

    TimeSpan TotalIdle { get; }
}
