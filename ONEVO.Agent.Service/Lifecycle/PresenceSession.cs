namespace ONEVO.Agent.Service.Lifecycle;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// In-memory presence-session tracker for Phase 1.
/// Service is source of truth for clock-in/break/idle/clock-out timing.
/// </summary>
public sealed class PresenceSession
{
    /// <summary>3× DeviceStateCollector's 60s tick — gap fallback threshold.</summary>
    public const int ActivityGapThresholdSeconds = 180;

    private readonly Lock _lock = new();
    private DateTimeOffset? _clockInAt;
    private DateTimeOffset? _clockOutAt;
    private (PauseReason Reason, DateTimeOffset StartedAt)? _openPause;
    private TimeSpan _accumulatedBreak;
    private TimeSpan _accumulatedIdle;
    private int _breakSessionCount;
    private string _scheduleDisplay = "Not configured";
    private Guid _sessionId;
    private DateTimeOffset _lastKnownActivityAt;
    private DateTimeOffset? _idleWatermark;
    private int? _breakAllowanceMinutes;
    private int _completedBreakMinutes;
    private bool _canStartBreak = true;
    private readonly List<SessionPauseInterval> _pauses = [];

    public void SetBreakAllowance(bool canStartBreak, int? allowanceMinutes, int completedBreakMinutes)
    {
        lock (_lock)
        {
            _canStartBreak = canStartBreak;
            _breakAllowanceMinutes = allowanceMinutes;
            _completedBreakMinutes = completedBreakMinutes;
        }
    }

    public bool HasActiveSession
    {
        get { lock (_lock) return _clockInAt is not null && _clockOutAt is null; }
    }

    /// <summary>Stable id for the current/most-recent session — the backend upsert key
    /// for the completed-session sync record enqueued at ClockOut.</summary>
    public Guid CurrentSessionId
    {
        get { lock (_lock) return _sessionId; }
    }

    public void ClockIn(DateTimeOffset at)
    {
        lock (_lock) StartSessionUnlocked(at, TimeSpan.Zero, at);
    }

    /// <summary>
    /// Rebuilds a session that is still open on the backend after this process restarted (AUD-001).
    /// Activity tracking starts at <paramref name="observedAt"/>, so the gap fallback does not turn
    /// the whole time since <paramref name="clockInAt"/> into idle, and the day's completed breaks
    /// come from the backend. The interval breakdown before the restart is unknown.
    /// </summary>
    public void ResumeOpenSession(DateTimeOffset clockInAt, TimeSpan completedBreak, DateTimeOffset observedAt)
    {
        lock (_lock) StartSessionUnlocked(clockInAt, completedBreak, observedAt);
    }

    private void StartSessionUnlocked(DateTimeOffset clockInAt, TimeSpan completedBreak, DateTimeOffset observedAt)
    {
        var trackingFrom = observedAt > clockInAt ? observedAt : clockInAt;

        _clockInAt = clockInAt;
        _clockOutAt = null;
        _openPause = null;
        _accumulatedBreak = completedBreak > TimeSpan.Zero ? completedBreak : TimeSpan.Zero;
        _accumulatedIdle = TimeSpan.Zero;
        _breakSessionCount = _accumulatedBreak > TimeSpan.Zero ? 1 : 0;
        _pauses.Clear();
        _sessionId = Guid.NewGuid();
        _lastKnownActivityAt = trackingFrom;
        _idleWatermark = trackingFrom;
    }

    public void StartBreak(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_clockInAt is null || _clockOutAt is not null)
                return;

            if (_openPause is { Reason: PauseReason.Idle })
                CloseOpenPauseUnlocked(at);

            if (_openPause is { Reason: PauseReason.ManualBreak })
                return;

            _openPause = (PauseReason.ManualBreak, at);
            _breakSessionCount++;
            _lastKnownActivityAt = at;
        }
    }

    public void EndBreak(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_openPause is not { Reason: PauseReason.ManualBreak })
                return;
            CloseOpenPauseUnlocked(at);
            _lastKnownActivityAt = at;
        }
    }

    /// <summary>
    /// Opens an auto-pause. Idempotent no-op (returns false) if any pause is already open —
    /// does not reset the start timestamp.
    /// </summary>
    public bool StartAutoPause(PauseReason reason, DateTimeOffset startedAt)
    {
        lock (_lock) return StartAutoPauseUnlocked(reason, startedAt);
    }

    /// <summary>
    /// Closes an auto-pause of <paramref name="reason"/> and adds its duration to the
    /// matching accumulator. No-op (returns false) if nothing is open or the open
    /// pause has a different reason.
    /// </summary>
    public bool EndAutoPause(PauseReason reason, DateTimeOffset endedAt)
    {
        lock (_lock) return EndAutoPauseUnlocked(reason, endedAt);
    }

    public void ClockOut(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_clockInAt is null)
                return;

            if (_openPause is not null)
                CloseOpenPauseUnlocked(at);

            _clockOutAt = at;
            _lastKnownActivityAt = at;
        }
    }

    /// <summary>
    /// Rebuilds a day that was already clocked out before this process started (Service restart),
    /// so the tray's finished-day screen shows real totals. Ignored while a session is open.
    /// </summary>
    public void RestoreCompletedDay(
        DateTimeOffset clockInAt,
        DateTimeOffset clockOutAt,
        TimeSpan accumulatedBreak,
        TimeSpan accumulatedIdle,
        int breakSessionCount)
    {
        lock (_lock)
        {
            if (_clockInAt is not null && _clockOutAt is null)
                return;

            _clockInAt = clockInAt;
            _clockOutAt = clockOutAt;
            _openPause = null;
            _accumulatedBreak = accumulatedBreak < TimeSpan.Zero ? TimeSpan.Zero : accumulatedBreak;
            _accumulatedIdle = accumulatedIdle < TimeSpan.Zero ? TimeSpan.Zero : accumulatedIdle;
            _breakSessionCount = Math.Max(0, breakSessionCount);
            _pauses.Clear();
            _lastKnownActivityAt = clockOutAt;
            _idleWatermark = clockOutAt;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _clockInAt = null;
            _clockOutAt = null;
            _openPause = null;
            _accumulatedBreak = TimeSpan.Zero;
            _accumulatedIdle = TimeSpan.Zero;
            _breakSessionCount = 0;
            _pauses.Clear();
        }
    }

    public void SetScheduleDisplay(string schedule)
    {
        lock (_lock)
            _scheduleDisplay = string.IsNullOrWhiteSpace(schedule)
                ? "Not configured"
                : schedule.Trim();
    }

    /// <summary>
    /// Call at the start of every inbound Tray IPC. Applies the gap fallback first,
    /// then stamps LastKnownActivityAt so a subsequent Snapshot does not re-count.
    /// </summary>
    public void ObserveInbound(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_clockInAt is not null && _clockOutAt is null)
                ApplyGapIfNeededUnlocked(at);
            _lastKnownActivityAt = at;
        }
    }

    public bool ApplyDeviceStateIdle(DeviceStateSnapshotPayload snapshot)
    {
        lock (_lock)
        {
            if (_clockInAt is null || _clockOutAt is not null)
                return false;

            if (snapshot.IsIdle)
            {
                var idleSeconds = Math.Max(0, snapshot.IdleSeconds);
                var start = snapshot.CapturedAt - TimeSpan.FromSeconds(idleSeconds);
                return StartAutoPauseUnlocked(PauseReason.Idle, start);
            }

            return EndAutoPauseUnlocked(PauseReason.Idle, snapshot.CapturedAt);
        }
    }

    public SessionSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_lock)
        {
            ApplyGapIfNeededUnlocked(now);

            var closedBreak = _accumulatedBreak < TimeSpan.Zero ? TimeSpan.Zero : _accumulatedBreak;
            var closedIdle = _accumulatedIdle < TimeSpan.Zero ? TimeSpan.Zero : _accumulatedIdle;

            var isOnBreak = _openPause is { Reason: PauseReason.ManualBreak };
            var isIdle = _openPause is { Reason: PauseReason.Idle };
            DateTimeOffset? breakStart = isOnBreak ? _openPause!.Value.StartedAt : null;
            DateTimeOffset? idleStart = isIdle ? _openPause!.Value.StartedAt : null;

            var breakTotalForWork = closedBreak;
            if (isOnBreak && breakStart is not null)
            {
                var open = now - breakStart.Value;
                if (open > TimeSpan.Zero)
                    breakTotalForWork += open;
            }

            var idleTotalForWork = closedIdle;
            if (isIdle && idleStart is not null)
            {
                var open = now - idleStart.Value;
                if (open > TimeSpan.Zero)
                    idleTotalForWork += open;
            }

            TimeSpan work = TimeSpan.Zero;
            if (_clockInAt is not null)
            {
                var end = _clockOutAt ?? now;
                var wall = end - _clockInAt.Value;
                work = wall - breakTotalForWork - idleTotalForWork;
                if (work < TimeSpan.Zero)
                    work = TimeSpan.Zero;
            }

            return new SessionSnapshot(
                ClockInAt: _clockInAt,
                ClockOutAt: _clockOutAt,
                IsOnBreak: isOnBreak,
                CurrentBreakStartedAt: breakStart,
                AccumulatedBreak: closedBreak,
                AccumulatedWork: work,
                ScheduleDisplay: _scheduleDisplay,
                BreakSessionCount: _breakSessionCount,
                AccumulatedIdle: closedIdle,
                IsIdle: isIdle,
                CurrentIdleStartedAt: idleStart,
                BreakAllowanceMinutes: _breakAllowanceMinutes,
                CompletedBreakMinutes: _completedBreakMinutes,
                CanStartBreak: _canStartBreak,
                Pauses: SnapshotPausesUnlocked(now));
        }
    }

    private bool StartAutoPauseUnlocked(PauseReason reason, DateTimeOffset startedAt)
    {
        if (_clockInAt is null || _clockOutAt is not null)
            return false;
        if (_openPause is not null)
            return false;

        var clamped = startedAt;
        if (clamped < _clockInAt.Value)
            clamped = _clockInAt.Value;
        if (reason == PauseReason.Idle && _idleWatermark is { } mark && clamped < mark)
            clamped = mark;

        _openPause = (reason, clamped);
        _lastKnownActivityAt = clamped;
        return true;
    }

    private bool EndAutoPauseUnlocked(PauseReason reason, DateTimeOffset endedAt)
    {
        if (_openPause is not { } open || open.Reason != reason)
            return false;
        CloseOpenPauseUnlocked(endedAt);
        return true;
    }

    private void CloseOpenPauseUnlocked(DateTimeOffset endedAt)
    {
        if (_openPause is not { } open)
            return;

        var duration = endedAt - open.StartedAt;
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        if (open.Reason == PauseReason.ManualBreak)
        {
            _accumulatedBreak += duration;
            if (_accumulatedBreak < TimeSpan.Zero)
                _accumulatedBreak = TimeSpan.Zero;
        }
        else
        {
            _accumulatedIdle += duration;
            if (_accumulatedIdle < TimeSpan.Zero)
                _accumulatedIdle = TimeSpan.Zero;
            _idleWatermark = endedAt;
        }

        RecordPauseUnlocked(open.Reason, open.StartedAt, open.StartedAt + duration);
        _openPause = null;
        _lastKnownActivityAt = endedAt;
    }

    private void RecordPauseUnlocked(PauseReason reason, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            return;

        var kind = reason == PauseReason.ManualBreak ? SessionPauseInterval.Break : SessionPauseInterval.Idle;
        _pauses.Add(new SessionPauseInterval(kind, start, end));

        // Bounded: on a very fragmented day the shortest stretch goes first, keeping the shape
        // of the timeline. The accumulators above stay exact either way.
        if (_pauses.Count > SessionPauseInterval.MaxPerSession)
        {
            var shortest = 0;
            for (var i = 1; i < _pauses.Count; i++)
            {
                if (_pauses[i].EndedAt - _pauses[i].StartedAt < _pauses[shortest].EndedAt - _pauses[shortest].StartedAt)
                    shortest = i;
            }
            _pauses.RemoveAt(shortest);
        }
    }

    private IReadOnlyList<SessionPauseInterval>? SnapshotPausesUnlocked(DateTimeOffset now)
    {
        if (_clockInAt is null)
            return null;
        // A day restored after a Service restart has totals but no intervals: report "unknown"
        // rather than an empty list the tray would draw as one unbroken active stretch.
        if (_pauses.Count == 0 && _openPause is null
            && (_accumulatedBreak > TimeSpan.Zero || _accumulatedIdle > TimeSpan.Zero))
            return null;

        var list = new List<SessionPauseInterval>(_pauses.Count + 1);
        list.AddRange(_pauses);
        if (_openPause is { } open && now > open.StartedAt)
        {
            var kind = open.Reason == PauseReason.ManualBreak ? SessionPauseInterval.Break : SessionPauseInterval.Idle;
            list.Add(new SessionPauseInterval(kind, open.StartedAt, now));
        }
        return list;
    }

    private void ApplyGapIfNeededUnlocked(DateTimeOffset now)
    {
        if (_clockInAt is null || _clockOutAt is not null)
            return;
        if (_openPause is not null)
            return;

        var gap = now - _lastKnownActivityAt;
        if (gap <= TimeSpan.FromSeconds(ActivityGapThresholdSeconds))
            return;

        _openPause = (PauseReason.Idle, _lastKnownActivityAt);
        CloseOpenPauseUnlocked(now);
    }
}
