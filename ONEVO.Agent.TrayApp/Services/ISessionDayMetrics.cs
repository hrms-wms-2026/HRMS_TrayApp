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

    /// <summary>
    /// Marks the Service presence session these metrics belong to, keyed by its clock-in time. A
    /// different clock-in than the one already tracked starts a fresh day via <see cref="ResetDay"/>;
    /// the same one (repeated status broadcasts) is a no-op.
    /// </summary>
    void BeginSession(DateTimeOffset clockInAt);

    /// <summary>Clears the collected day data. <see cref="LastCompletedSession"/> is kept.</summary>
    void ResetDay();

    IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5);

    IReadOnlyList<double> GetHourlyFocusFractions();

    /// <summary>Local clock hour (0-23) with the most focus time today, or null when none was tracked.</summary>
    int? GetPeakFocusHour() => null;

    IReadOnlyList<SessionScreenshot> GetAllowedScreenshots();

    IReadOnlyList<SessionScreenshot> GetActivityChecks();

    TimeSpan TotalIdle { get; }
}
