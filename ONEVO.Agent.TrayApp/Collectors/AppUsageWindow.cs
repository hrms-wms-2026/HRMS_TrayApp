namespace ONEVO.Agent.TrayApp.Collectors;

/// <summary>
/// Accumulates foreground time per process across one app-usage sample window so app switches
/// inside the window are captured locally. The backend counts every AppUsageSnapshot as one
/// minute, so the window still closes into a single snapshot for the dominant app — emitting
/// one snapshot per switch would inflate usage.
/// </summary>
internal sealed class AppUsageWindow
{
    // Dictionary keys cannot be null; a foreground window whose process could not be resolved
    // is tracked under this key and reported back as a null process name.
    private const string UnknownProcessKey = "";

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastProcessKey;
    private long _sequence;

    /// <summary>Records <paramref name="elapsed"/> of foreground time for the observed app.</summary>
    /// <returns><c>true</c> when this is the first observation or the foreground process differs from the previous one.</returns>
    public bool Observe(string? processName, string titleHash, TimeSpan elapsed)
    {
        var key = string.IsNullOrWhiteSpace(processName) ? UnknownProcessKey : processName;
        var changed = _lastProcessKey is null
            || !string.Equals(_lastProcessKey, key, StringComparison.OrdinalIgnoreCase);
        _lastProcessKey = key;

        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry();
            _entries[key] = entry;
        }

        entry.Duration += elapsed;
        entry.TitleHash = titleHash;
        entry.LastSeen = ++_sequence;
        return changed;
    }

    /// <summary>
    /// Closes the window: returns the app with the most foreground time (ties go to the most
    /// recently observed) plus per-process durations, then resets for the next window.
    /// Returns <c>null</c> when nothing was observed.
    /// </summary>
    public AppUsageWindowResult? Complete()
    {
        if (_entries.Count == 0)
            return null;

        var dominant = _entries
            .OrderByDescending(pair => pair.Value.Duration)
            .ThenByDescending(pair => pair.Value.LastSeen)
            .First();

        var durations = _entries
            .Where(pair => pair.Key != UnknownProcessKey)
            .Select(pair => (pair.Key, pair.Value.Duration))
            .ToList();

        var result = new AppUsageWindowResult(
            dominant.Key == UnknownProcessKey ? null : dominant.Key,
            dominant.Value.TitleHash,
            durations);

        _entries.Clear();
        return result;
    }

    private sealed class Entry
    {
        public TimeSpan Duration;
        public string TitleHash = string.Empty;
        public long LastSeen;
    }
}

internal sealed record AppUsageWindowResult(
    string? DominantProcessName,
    string DominantTitleHash,
    IReadOnlyList<(string ProcessName, TimeSpan Duration)> ProcessDurations);
