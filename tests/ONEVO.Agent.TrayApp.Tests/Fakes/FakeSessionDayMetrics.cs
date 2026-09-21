namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

/// <summary>Minimal in-memory <see cref="ISessionDayMetrics"/> fake for collector tests that only
/// need to observe idle-sample accumulation, not the full day-metrics behavior.</summary>
public sealed class FakeSessionDayMetrics : ISessionDayMetrics
{
    public SessionSnapshot? LastCompletedSession { get; private set; }

    public TimeSpan TotalIdle { get; private set; }

    public int AddIdleSampleCallCount { get; private set; }

    public List<SessionScreenshot> AllowedScreenshots { get; } = [];

    public void RememberCompletedSession(SessionSnapshot session) => LastCompletedSession = session;

    public void AddAppUsageSample(string processName, TimeSpan sampleWindow) { }

    public void AddIdleSample(TimeSpan idlePortion)
    {
        AddIdleSampleCallCount++;
        TotalIdle += idlePortion;
    }

    public void AddAllowedScreenshot(Guid attemptId, DateTimeOffset capturedAt, ReadOnlyMemory<byte> jpegBytes)
    {
        if (jpegBytes.IsEmpty)
            return;
        AllowedScreenshots.Add(new SessionScreenshot(attemptId, capturedAt, jpegBytes.ToArray()));
    }

    public void ResetDay()
    {
        TotalIdle = TimeSpan.Zero;
        LastCompletedSession = null;
        AllowedScreenshots.Clear();
    }

    public IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5) => [];

    public IReadOnlyList<SessionScreenshot> GetAllowedScreenshots() => AllowedScreenshots;
}
