namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using ONEVO.Agent.Shared.IPC;

public sealed class SessionDayMetrics : ISessionDayMetrics
{
    internal const int MaxAllowedScreenshots = 12;
    internal const int MaxActivityChecks = MaxAllowedScreenshots;

    private readonly ConcurrentDictionary<string, TimeSpan> _appSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, TimeSpan> _hourlyFocus = new();
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

    public void AddAppUsageSample(string processName, TimeSpan sampleWindow) =>
        AddAppUsageSample(processName, sampleWindow, DateTimeOffset.Now);

    /// <summary>Testing seam: lets tests control which hour a sample lands in. Not part of
    /// <see cref="ISessionDayMetrics"/> — production callers always go through the 2-arg overload,
    /// which stamps the current local time.</summary>
    internal void AddAppUsageSample(string processName, TimeSpan sampleWindow, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(processName) || sampleWindow <= TimeSpan.Zero)
            return;

        var key = processName.Trim();
        _appSeconds.AddOrUpdate(key, sampleWindow, (_, prev) => prev + sampleWindow);
        _hourlyFocus.AddOrUpdate(at.Hour, sampleWindow, (_, prev) => prev + sampleWindow);
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
        _hourlyFocus.Clear();
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

    public IReadOnlyList<double> GetHourlyFocusFractions()
    {
        var snapshot = _hourlyFocus.ToArray();
        if (snapshot.Length == 0)
            return [];

        var max = snapshot.Max(kv => kv.Value.TotalSeconds);
        if (max <= 0)
            return [];

        return snapshot
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value.TotalSeconds / max)
            .ToList();
    }

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
