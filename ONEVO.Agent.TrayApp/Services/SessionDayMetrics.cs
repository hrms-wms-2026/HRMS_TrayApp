namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using ONEVO.Agent.Shared.IPC;

public sealed class SessionDayMetrics : ISessionDayMetrics
{
    private readonly ConcurrentDictionary<string, TimeSpan> _appSeconds = new(StringComparer.OrdinalIgnoreCase);
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

    public void ResetDay()
    {
        _appSeconds.Clear();
        lock (_gate)
        {
            _idle = TimeSpan.Zero;
            _lastCompleted = null;
        }
    }

    public IReadOnlyList<(string Name, TimeSpan Duration)> GetTopApps(int take = 5) =>
        _appSeconds
            .Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(x => x.Value)
            .Take(Math.Max(1, take))
            .ToList();
}
