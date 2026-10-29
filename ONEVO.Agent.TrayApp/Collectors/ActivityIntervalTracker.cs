namespace ONEVO.Agent.TrayApp.Collectors;

/// <summary>
/// Active/idle accounting for one snapshot interval from periodic last-input samples (ISS-014).
/// A span with no keyboard/mouse input lasting at least the idle threshold counts as idle from
/// its first second; everything else in the interval is active. Reported time is the time that
/// actually elapsed, capped at the interval length, so a partial interval (pause, stop,
/// disconnect) never reports a full interval and time spent asleep/hibernated is not reported.
/// Accuracy: the end of an idle span is detected at sample resolution.
/// </summary>
internal sealed class ActivityIntervalTracker
{
    private readonly int _intervalSeconds;
    private readonly int _idleThresholdSeconds;
    private long _intervalStartMs;
    private long _lastSampleMs;
    private long _idleMs;
    private bool _idle;

    public ActivityIntervalTracker(int intervalSeconds, int idleThresholdSeconds, long nowMs)
    {
        _intervalSeconds = intervalSeconds;
        _idleThresholdSeconds = idleThresholdSeconds;
        Restart(nowMs);
    }

    public void Restart(long nowMs)
    {
        _intervalStartMs = nowMs;
        _lastSampleMs = nowMs;
        _idleMs = 0;
        _idle = false;
    }

    public long ElapsedMs(long nowMs) => nowMs - _intervalStartMs;

    public void Sample(long nowMs, int secondsSinceLastInput)
    {
        var idleNow = secondsSinceLastInput >= _idleThresholdSeconds;
        if (idleNow)
        {
            var lastInputMs = nowMs - secondsSinceLastInput * 1000L;
            // Newly detected idle span: count it from the last input (not from detection).
            // Continuing span: count since the previous sample, unless input happened since.
            var fromMs = _idle
                ? Math.Max(_lastSampleMs, lastInputMs)
                : Math.Max(_intervalStartMs, lastInputMs);
            _idleMs += Math.Max(0, nowMs - fromMs);
        }
        _idle = idleNow;
        _lastSampleMs = nowMs;
    }

    /// <summary>Closes the current interval and starts the next one at <paramref name="nowMs"/>.</summary>
    public (int ActiveSeconds, int IdleSeconds) Complete(long nowMs)
    {
        var totalSeconds = (int)Math.Clamp(Math.Round((nowMs - _intervalStartMs) / 1000.0), 0, _intervalSeconds);
        var idleSeconds = (int)Math.Clamp(Math.Round(_idleMs / 1000.0), 0, totalSeconds);

        var stillIdle = _idle;
        Restart(nowMs);
        _idle = stillIdle; // an idle span continuing across the boundary is counted from the new start

        return (totalSeconds - idleSeconds, idleSeconds);
    }
}
