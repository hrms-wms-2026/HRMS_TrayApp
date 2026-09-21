namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// One activity-check tile for Daily Summary / PDF. Allowed shots keep JPEG bytes in
/// process memory only; skipped checks are a red note with no image.
/// </summary>
public sealed record SessionScreenshot(
    Guid AttemptId,
    DateTimeOffset CapturedAt,
    byte[] JpegBytes,
    bool IsSkipped = false);

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

    void AddSkippedScreenshot(Guid attemptId, DateTimeOffset skippedAt);

    void ResetDay();

    IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5);

    IReadOnlyList<SessionScreenshot> GetAllowedScreenshots();

    IReadOnlyList<SessionScreenshot> GetActivityChecks();

    TimeSpan TotalIdle { get; }
}
