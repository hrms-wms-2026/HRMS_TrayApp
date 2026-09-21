namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using ONEVO.Agent.Shared.IPC;

public sealed class SessionDayMetrics : ISessionDayMetrics
{
    internal const int MaxAllowedScreenshots = 12;
    internal const int MaxActivityChecks = MaxAllowedScreenshots;

    private readonly ConcurrentDictionary<string, TimeSpan> _appSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SessionScreenshot> _screenshots = [];
    private readonly object _gate = new();
    private TimeSpan _idle;
    private SessionSnapshot? _lastCompleted;

    public SessionSnapshot? LastCompletedSession
    {
        get { lock (_gate) return _lastCompleted; }
    }

    public TimeSpan TotalIdle
    {
        get { lock (_gate) return _idle; }
    }

    public void RememberCompletedSession(SessionSnapshot session)
    {
        lock (_gate)
            _lastCompleted = session;
    }

    public void AddAppUsageSample(string processName, TimeSpan sampleWindow)
    {
        if (string.IsNullOrWhiteSpace(processName) || sampleWindow <= TimeSpan.Zero)
            return;

        var key = processName.Trim();
        _appSeconds.AddOrUpdate(key, sampleWindow, (_, prev) => prev + sampleWindow);
    }

    public void AddIdleSample(TimeSpan idlePortion)
    {
        if (idlePortion <= TimeSpan.Zero) return;
        lock (_gate)
            _idle += idlePortion;
    }

    public void AddAllowedScreenshot(Guid attemptId, DateTimeOffset capturedAt, ReadOnlyMemory<byte> jpegBytes)
    {
        if (jpegBytes.IsEmpty)
            return;

        lock (_gate)
            AppendActivityCheck(new SessionScreenshot(attemptId, capturedAt, jpegBytes.ToArray()));
    }

    public void AddSkippedScreenshot(Guid attemptId, DateTimeOffset skippedAt)
    {
        lock (_gate)
            AppendActivityCheck(new SessionScreenshot(attemptId, skippedAt, [], IsSkipped: true));
    }

    private void AppendActivityCheck(SessionScreenshot item)
    {
        _screenshots.Add(item);
        while (_screenshots.Count > MaxActivityChecks)
            _screenshots.RemoveAt(0);
    }

    public void ResetDay()
    {
        _appSeconds.Clear();
        lock (_gate)
        {
            _idle = TimeSpan.Zero;
            _lastCompleted = null;
            _screenshots.Clear();
        }
    }

    public IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5) =>
        _appSeconds
            .Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(x => x.Value)
            .Take(Math.Max(1, take))
            .ToList();

    public IReadOnlyList<SessionScreenshot> GetAllowedScreenshots()
    {
        lock (_gate)
            return _screenshots.Where(s => !s.IsSkipped).ToArray();
    }

    public IReadOnlyList<SessionScreenshot> GetActivityChecks()
    {
        lock (_gate)
            return _screenshots.ToArray();
    }
}
