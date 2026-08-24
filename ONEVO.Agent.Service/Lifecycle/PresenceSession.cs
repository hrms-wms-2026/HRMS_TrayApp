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
    private string _scheduleDisplay = "09:00 AM – 06:00 PM";
    private Guid _sessionId;
    private DateTimeOffset _lastKnownActivityAt;
    private DateTimeOffset? _idleWatermark;

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
        lock (_lock)
        {
            _clockInAt = at;
            _clockOutAt = null;
            _openPause = null;
            _accumulatedBreak = TimeSpan.Zero;
            _accumulatedIdle = TimeSpan.Zero;
            _breakSessionCount = 0;
            _sessionId = Guid.NewGuid();
            _lastKnownActivityAt = at;
            _idleWatermark = at;
        }
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
        }
    }

    public void SetScheduleDisplay(string schedule)
    {
        lock (_lock)
            _scheduleDisplay = string.IsNullOrWhiteSpace(schedule)
                ? "09:00 AM – 06:00 PM"
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
                CurrentIdleStartedAt: idleStart);
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

        _openPause = null;
        _lastKnownActivityAt = endedAt;
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
