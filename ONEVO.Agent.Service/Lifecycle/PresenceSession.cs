namespace ONEVO.Agent.Service.Lifecycle;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// In-memory presence-session tracker for Phase 1.
/// Service is source of truth for clock-in/break/clock-out timing.
/// </summary>
public sealed class PresenceSession
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _clockInAt;
    private DateTimeOffset? _clockOutAt;
    private bool _isOnBreak;
    private DateTimeOffset? _currentBreakStartedAt;
    private TimeSpan _accumulatedBreak;
    private int _breakSessionCount;
    private string _scheduleDisplay = "09:00 AM – 06:00 PM";

    public bool HasActiveSession
    {
        get { lock (_lock) return _clockInAt is not null && _clockOutAt is null; }
    }

    public void ClockIn(DateTimeOffset at)
    {
        lock (_lock)
        {
            _clockInAt = at;
            _clockOutAt = null;
            _isOnBreak = false;
            _currentBreakStartedAt = null;
            _accumulatedBreak = TimeSpan.Zero;
            _breakSessionCount = 0;
        }
    }

    public void StartBreak(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_clockInAt is null || _clockOutAt is not null)
                return;
            if (_isOnBreak)
                return;

            _isOnBreak = true;
            _currentBreakStartedAt = at;
            _breakSessionCount++;
        }
    }

    public void EndBreak(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (!_isOnBreak || _currentBreakStartedAt is null)
                return;

            _accumulatedBreak += at - _currentBreakStartedAt.Value;
            if (_accumulatedBreak < TimeSpan.Zero)
                _accumulatedBreak = TimeSpan.Zero;

            _isOnBreak = false;
            _currentBreakStartedAt = null;
        }
    }

    public void ClockOut(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_clockInAt is null)
                return;

            // Finalize open break into accumulated break.
            if (_isOnBreak && _currentBreakStartedAt is not null)
            {
                _accumulatedBreak += at - _currentBreakStartedAt.Value;
                if (_accumulatedBreak < TimeSpan.Zero)
                    _accumulatedBreak = TimeSpan.Zero;
                _isOnBreak = false;
                _currentBreakStartedAt = null;
            }

            _clockOutAt = at;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _clockInAt = null;
            _clockOutAt = null;
            _isOnBreak = false;
            _currentBreakStartedAt = null;
            _accumulatedBreak = TimeSpan.Zero;
            _breakSessionCount = 0;
        }
    }

    public void SetScheduleDisplay(string schedule)
    {
        lock (_lock)
            _scheduleDisplay = string.IsNullOrWhiteSpace(schedule)
                ? "09:00 AM – 06:00 PM"
                : schedule.Trim();
    }

    public SessionSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_lock)
        {
            var breakTotal = _accumulatedBreak;
            if (_isOnBreak && _currentBreakStartedAt is not null)
            {
                var open = now - _currentBreakStartedAt.Value;
                if (open > TimeSpan.Zero)
                    breakTotal += open;
            }

            TimeSpan work = TimeSpan.Zero;
            if (_clockInAt is not null)
            {
                var end = _clockOutAt ?? now;
                var wall = end - _clockInAt.Value;
                work = wall - breakTotal;
                if (work < TimeSpan.Zero)
                    work = TimeSpan.Zero;
            }

            return new SessionSnapshot(
                ClockInAt: _clockInAt,
                ClockOutAt: _clockOutAt,
                IsOnBreak: _isOnBreak,
                CurrentBreakStartedAt: _currentBreakStartedAt,
                AccumulatedBreak: breakTotal,
                AccumulatedWork: work,
                ScheduleDisplay: _scheduleDisplay,
                BreakSessionCount: _breakSessionCount);
        }
    }
}
