# Idle Time Exclusion (Input-Idle + Sleep) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exclude input-idle time and machine-sleep time from payroll-relevant `AccumulatedWork`, rolling both into a single **Idle** bucket distinct from manual **Break**.

**Architecture:** `PresenceSession` becomes the single source of truth. It owns one open-pause slot (`ManualBreak` | `Idle`) so idle and break cannot overlap. Three detection paths all call `StartAutoPause`/`EndAutoPause` with `PauseReason.Idle`: TrayApp `DeviceStateSnapshot` IPC (existing 60s collector, no new IPC), in-process `SystemEvents.PowerModeChanged` (Service-only), and a Snapshot/inbound-IPC gap fallback (~180s). `AccumulatedWork = wallClock - AccumulatedBreak - AccumulatedIdle`. Manual break lifecycle (state machine `Paused`, collector stop) is unchanged — idle does **not** transition monitoring state.

**Tech Stack:** .NET 10 (MAUI TrayApp + Windows Service), SQLite (`ActivityRecordBuffer`), ASP.NET Core / EF Core 8 / PostgreSQL (HRMS-Backend-v1), xUnit.

**Spec:** `tray_app_maui/docs/superpowers/specs/2026-08-21-idle-sleep-time-exclusion-design.md`

**Repos:** `tray_app_maui` and `HRMS-Backend-v1` are separate git roots. Commit in the repo that owns the files. Do not commit the other repo's files in the same commit.

**Architecture-doc checklist (§20):** Phase 1 Windows-only; no new collector; no new IPC message type; no Device JWT in TrayApp; keyboard/mouse remain counts-only (`GetLastInputInfo` already in `IdleDetector`); Service remains source of truth for session math.

---

## Design locks (spec open items, resolved here)

1. **SQLite:** add `session_history.accumulated_idle_sec REAL NOT NULL DEFAULT 0` via `PRAGMA table_info` + `ALTER TABLE` (existing local DBs have no EF migrations).
2. **Backend:** add `employee_work_sessions.accumulated_idle_seconds` (integer, default 0). Ingest DTO field `accumulated_idle_seconds` defaults to 0 so old in-flight agent payloads still upsert. Daily report gains `IdleMinutes`. No Angular consumer of these fields exists today — do **not** add a frontend-web task.
3. **TrayApp active-session UI:** replace the placeholder **Tasks Completed** cell with **Idle Time**. Break Time stays. No idle prompt/toast (spec non-goal).
4. **`SessionSnapshot` extra fields:** spec named `AccumulatedIdle`. Also add `IsIdle` + `CurrentIdleStartedAt`, mirroring the break contract (`AccumulatedBreak` is closed-only; tray ticks the open segment locally). Without those, the live work timer would jump only on the next 60s DeviceState / status push.
5. **`LastKnownActivityAt`:** update only on inbound Tray IPC (after applying the gap check) and on power **Resume**. Do **not** update from `HeartbeatService` / sync ticks — those keep running while the Service process is alive and would disable the sleep fallback. Do **not** apply the gap inside `ClockOut` itself: production ClockOut always arrives as IPC (so `ObserveInbound` already ran), and applying it in `ClockOut` would make unit tests that never call `ObserveInbound` treat the whole shift as idle.
6. **Idle start clamp:** `StartAutoPause(Idle, startedAt)` clamps `startedAt` to `max(startedAt, ClockInAt, lastIdleEndedAt)` so a post-resume `DeviceStateSnapshot` whose `IdleSeconds` still includes sleep cannot double-count time already closed by `EndAutoPause`.
7. **Break while idle:** `StartBreak` closes the open Idle pause at the break timestamp, then opens `ManualBreak`. Reverse path (idle while break is open) stays a no-op.
8. **PowerMode reliability:** implement the listener behind `ISystemPowerEvents` plus the gap fallback. A real suspend/resume on this Service (`UseWindowsService` / Session 0) is a **manual verification** task at the end — not a gate that blocks the rest of the work.

---

## File Structure

| File | Responsibility |
|---|---|
| `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PauseReason.cs` | `ManualBreak`, `Idle` |
| `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PresenceSession.cs` | Open-pause slot, `AccumulatedIdle`, `StartAutoPause`/`EndAutoPause`, `ApplyDeviceStateIdle`, `ObserveInbound` gap fallback |
| `tray_app_maui/ONEVO.Agent.Shared/IPC/IpcMessages.cs` | `SessionSnapshot` + `AccumulatedIdle`/`IsIdle`/`CurrentIdleStartedAt` |
| `tray_app_maui/ONEVO.Agent.Shared/Models/CollectionRecord.cs` | `WorkSessionPayload.AccumulatedIdle` |
| `tray_app_maui/ONEVO.Agent.Service/AgentWorker.cs` | `ObserveInbound` on every IPC; apply DeviceState idle; broadcast status; persist idle |
| `tray_app_maui/ONEVO.Agent.Service/Lifecycle/ISystemPowerEvents.cs` | Test seam for power events |
| `tray_app_maui/ONEVO.Agent.Service/Lifecycle/SystemPowerEvents.cs` | `Microsoft.Win32.SystemEvents.PowerModeChanged` adapter |
| `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PowerModeIdleListener.cs` | Hosted service: Suspend → `StartAutoPause(Idle)`, Resume → `EndAutoPause(Idle)` |
| `tray_app_maui/ONEVO.Agent.Service/Program.cs` | Register power events + listener |
| `tray_app_maui/ONEVO.Agent.Service/Buffer/ActivityRecordBuffer.cs` | `accumulated_idle_sec` column + `SaveSessionHistory` param |
| `tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs` | `accumulated_idle_seconds` on submit request |
| `tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs` | Map idle seconds onto the submit body |
| `tray_app_maui/ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs` | Tick idle locally; subtract idle from work |
| `tray_app_maui/ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml` | Idle Time cell |
| `tray_app_maui/ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs` | Idle/productive from `AccumulatedIdle`, not `SessionDayMetrics.TotalIdle` |
| `HRMS-Backend-v1/src/ONEVO.Domain/Features/Monitoring/WorkSessions/Entities/EmployeeWorkSession.cs` | `AccumulatedIdleSeconds` |
| `HRMS-Backend-v1/src/ONEVO.Infrastructure/Migrations/<generated>_AddWorkSessionAccumulatedIdle.cs` | Column + default 0 |
| `HRMS-Backend-v1` ingest command/controller/validator + daily report DTO/handler | Persist and report Idle |

---

### Task 1: Shared IPC contract — `SessionSnapshot` idle fields

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Shared/IPC/IpcMessages.cs:79-87`

- [ ] **Step 1: Add idle fields with defaults so existing 8-arg constructors still compile**

Replace the `SessionSnapshot` record with:

```csharp
/// <summary>Authoritative presence-session snapshot owned by the Service.</summary>
public sealed record SessionSnapshot(
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    bool IsOnBreak,
    DateTimeOffset? CurrentBreakStartedAt,
    TimeSpan AccumulatedBreak,
    TimeSpan AccumulatedWork,
    string? ScheduleDisplay,
    int BreakSessionCount,
    TimeSpan AccumulatedIdle = default,
    bool IsIdle = false,
    DateTimeOffset? CurrentIdleStartedAt = null);
```

`AccumulatedIdle` is **closed idle only**, same contract as `AccumulatedBreak`. Open idle is `IsIdle` + `CurrentIdleStartedAt`.

- [ ] **Step 2: Build Shared**

Run from `tray_app_maui`:

```powershell
dotnet build .\ONEVO.Agent.Shared\ONEVO.Agent.Shared.csproj
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs
git commit -m "feat: add idle fields to SessionSnapshot IPC contract"
```

---

### Task 2: `PresenceSession` — unified pause slot + Idle accumulator (TDD)

**Files:**
- Create: `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PauseReason.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PresenceSession.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs`

- [ ] **Step 1: Write the failing tests (append to `PresenceSessionTests.cs`; keep the three existing tests)**

```csharp
    [Fact]
    public void StartAutoPause_EndAutoPause_Idle_AccumulatesIntoIdle_NotBreak()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(10);
        var t3 = t2.AddHours(1);

        session.ClockIn(t0);
        Assert.True(session.StartAutoPause(PauseReason.Idle, t1));
        Assert.True(session.EndAutoPause(PauseReason.Idle, t2));
        session.ClockOut(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedBreak);
        Assert.False(snap.IsIdle);
        // 1h40m wall - 10m idle = 1h30m work
        Assert.Equal(TimeSpan.FromMinutes(90), snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_WhileIdle_ClosedIdleOnly_OpenIdleInWorkMath()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var now = t1.AddMinutes(5);

        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t1);

        var snap = session.Snapshot(now);
        Assert.True(snap.IsIdle);
        Assert.Equal(t1, snap.CurrentIdleStartedAt);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromHours(1), snap.AccumulatedWork);
    }

    [Fact]
    public void DuplicateStartAutoPause_DoesNotResetIdleStart()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(10);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(10);

        session.ClockIn(t0);
        Assert.True(session.StartAutoPause(PauseReason.Idle, t1));
        Assert.False(session.StartAutoPause(PauseReason.Idle, t2)); // suspend/duplicate IsIdle
        Assert.True(session.EndAutoPause(PauseReason.Idle, t3));

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(15), snap.AccumulatedIdle);
    }

    [Fact]
    public void IdleDetection_WhileManualBreakOpen_IsNoOp()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(5);
        var t3 = t1.AddMinutes(20);

        session.ClockIn(t0);
        session.StartBreak(t1);
        Assert.False(session.StartAutoPause(PauseReason.Idle, t2));
        Assert.False(session.EndAutoPause(PauseReason.Idle, t3));
        session.EndBreak(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(20), snap.AccumulatedBreak);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(30), snap.AccumulatedWork);
    }

    [Fact]
    public void StartBreak_WhileIdleOpen_ClosesIdleThenOpensBreak()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var idleStart = t0.AddMinutes(20);
        var breakStart = t0.AddMinutes(30);
        var breakEnd = t0.AddMinutes(40);

        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, idleStart);
        session.StartBreak(breakStart);
        session.EndBreak(breakEnd);

        var snap = session.Snapshot(breakEnd);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedBreak);
        Assert.False(snap.IsIdle);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromMinutes(20), snap.AccumulatedWork);
    }

    [Fact]
    public void ClockOut_WhileIdleOpen_FinalizesIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(2);
        var t2 = t1.AddMinutes(15);

        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t1);
        session.ClockOut(t2);

        var snap = session.Snapshot(t2);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(15), snap.AccumulatedIdle);
        Assert.Equal(t2, snap.ClockOutAt);
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedWork);
    }

    [Fact]
    public void ExistingBreakMath_UnchangedWhenNoIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(10);
        var t3 = t2.AddHours(1);

        session.ClockIn(t0);
        session.StartBreak(t1);
        session.EndBreak(t2);
        session.ClockOut(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedBreak);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(90), snap.AccumulatedWork);
        Assert.Equal(1, snap.BreakSessionCount);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PresenceSessionTests" --nologo
```

Expected: FAIL — `StartAutoPause` / `PauseReason` / `AccumulatedIdle` do not exist.

- [ ] **Step 3: Add `PauseReason` and rewrite `PresenceSession`**

Create `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PauseReason.cs`:

```csharp
namespace ONEVO.Agent.Service.Lifecycle;

public enum PauseReason
{
    ManualBreak,
    Idle
}
```

Replace `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PresenceSession.cs` in full with:

```csharp
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
        }
    }

    public void EndBreak(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_openPause is not { Reason: PauseReason.ManualBreak })
                return;
            CloseOpenPauseUnlocked(at);
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

            // Gap fallback is applied by ObserveInbound (every inbound IPC, including
            // ClockOut) and by Snapshot. Do not apply it here — unit tests and any
            // ClockOut that skipped ObserveInbound would otherwise treat the whole
            // session as one idle gap because LastKnownActivityAt is still ClockInAt.
            if (_openPause is not null)
                CloseOpenPauseUnlocked(at);

            _clockOutAt = at;
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
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PresenceSessionTests" --nologo
```

Expected: all PASS (existing three + new).

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.Service/Lifecycle/PauseReason.cs ONEVO.Agent.Service/Lifecycle/PresenceSession.cs tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs
git commit -m "feat: exclude idle from AccumulatedWork via unified pause slot"
```

---

### Task 3: DeviceState idle path + gap fallback tests

**Files:**
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs` (append)
- Implementation already in Task 2 (`ApplyDeviceStateIdle`, `ObserveInbound`)

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ApplyDeviceStateIdle_True_BackDatesStartFromIdleSeconds()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var captured = t0.AddMinutes(5);
        session.ClockIn(t0);

        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = captured,
            IdleSeconds = 180,
            IsIdle = true
        }));

        var snap = session.Snapshot(captured);
        Assert.True(snap.IsIdle);
        Assert.Equal(captured - TimeSpan.FromSeconds(180), snap.CurrentIdleStartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), snap.AccumulatedWork); // 5m wall - 3m open idle
    }

    [Fact]
    public void ApplyDeviceStateIdle_Sequence_CrossingThresholdBothDirections()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);

        var tActive = t0.AddMinutes(1);
        Assert.False(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tActive, IdleSeconds = 10, IsIdle = false
        }));

        var tIdle = t0.AddMinutes(4);
        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tIdle, IdleSeconds = 150, IsIdle = true
        }));

        var tStillIdle = t0.AddMinutes(5);
        Assert.False(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tStillIdle, IdleSeconds = 210, IsIdle = true
        }));

        var tResume = t0.AddMinutes(6);
        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tResume, IdleSeconds = 2, IsIdle = false
        }));

        var snap = session.Snapshot(tResume);
        Assert.False(snap.IsIdle);
        // start = tIdle - 150s = t0+4m-2.5m = t0+1.5m; end = t0+6m → 4.5m idle
        Assert.Equal(TimeSpan.FromMinutes(4.5), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(1.5), snap.AccumulatedWork);
    }

    [Fact]
    public void ApplyDeviceStateIdle_AfterSleepClose_DoesNotDoubleCountViaIdleSeconds()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var sleepStart = t0.AddHours(1);
        var sleepEnd = t0.AddHours(3);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, sleepStart);
        session.EndAutoPause(PauseReason.Idle, sleepEnd);

        // GetLastInputInfo after resume still reports 2h idle (includes sleep).
        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = sleepEnd,
            IdleSeconds = (int)TimeSpan.FromHours(2).TotalSeconds,
            IsIdle = true
        }));

        var snap = session.Snapshot(sleepEnd);
        Assert.True(snap.IsIdle);
        Assert.Equal(sleepEnd, snap.CurrentIdleStartedAt); // clamped to idle watermark
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromHours(1), snap.AccumulatedWork);
    }

    [Fact]
    public void ObserveInbound_GapOverThreshold_RetroactivelyAddsIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);

        var later = t0.AddMinutes(10);
        session.ObserveInbound(later);

        var snap = session.Snapshot(later);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
    }

    [Fact]
    public void ObserveInbound_GapUnderThreshold_DoesNotAddIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);
        session.ObserveInbound(t0.AddMinutes(2));

        var snap = session.Snapshot(t0.AddMinutes(2));
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(2), snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_GapFallback_WhenNoPauseOpen()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0); // lastKnown = t0
        var now = t0.AddMinutes(5);
        var snap = session.Snapshot(now);
        Assert.Equal(TimeSpan.FromMinutes(5), snap.AccumulatedIdle);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_GapFallback_SkippedWhenIdleAlreadyOpen()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t0.AddMinutes(1));
        var now = t0.AddMinutes(10);
        var snap = session.Snapshot(now);
        Assert.True(snap.IsIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle); // still open, not retro-closed
        Assert.Equal(TimeSpan.FromMinutes(1), snap.AccumulatedWork);
    }

    [Fact]
    public void EndAutoPause_WrongReason_IsNoOp()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t0.AddMinutes(1));
        Assert.False(session.EndAutoPause(PauseReason.ManualBreak, t0.AddMinutes(2)));
        var snap = session.Snapshot(t0.AddMinutes(2));
        Assert.True(snap.IsIdle);
    }

    [Fact]
    public void ClockOut_AfterObserveInboundGap_DoesNotDoubleCountIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var tOut = t0.AddHours(2);
        session.ClockIn(t0);
        session.ObserveInbound(tOut);
        session.ClockOut(tOut);

        var snap = session.Snapshot(tOut);
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
        Assert.False(snap.IsIdle);
    }
```

Add `using ONEVO.Agent.Shared.Models;` at the top of the test file if it is not already there.

- [ ] **Step 2: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PresenceSessionTests" --nologo
```

Expected: PASS — Task 2 already implemented `ApplyDeviceStateIdle` / `ObserveInbound` / gap check. If any fail, fix `PresenceSession` (do not weaken the tests).

- [ ] **Step 3: Commit**

```powershell
git add tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs
git commit -m "test: cover DeviceState idle path and gap-detection fallback"
```

If `PresenceSession.cs` also changed to make a test pass, include it in the same commit.

---

### Task 4: AgentWorker — observe inbound IPC, apply DeviceState idle, broadcast, persist

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Service/AgentWorker.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Lifecycle/DeviceStateIdleHandlerTests.cs` (new — tests the PresenceSession API the handler calls, plus a local replica of the record-loop so SessionSnapshot idle is asserted without spinning up the hosted worker)

- [ ] **Step 1: Write the integration-style test**

Create `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Lifecycle/DeviceStateIdleHandlerTests.cs`:

```csharp
using System.Text.Json;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

public sealed class DeviceStateIdleHandlerTests
{
    private static CollectionRecord DeviceState(DateTimeOffset captured, int idleSeconds, bool isIdle) =>
        new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            RecordType = CollectionRecordTypes.DeviceStateSnapshot,
            SchemaVersion = CollectionSchemaVersions.DeviceStateSnapshotV1,
            CaptureTimestamp = captured,
            DeviceId = "test",
            Payload = JsonSerializer.SerializeToElement(new DeviceStateSnapshotPayload
            {
                CapturedAt = captured,
                IdleSeconds = idleSeconds,
                IsIdle = isIdle
            })
        };

    /// <summary>
    /// Mirrors AgentWorker.HandleCollectionSubmitAsync's DeviceState branch:
    /// deserialize + ApplyDeviceStateIdle. The worker itself is a hosted service
    /// with many deps; this is the behavior under test.
    /// </summary>
    private static void ApplyRecords(PresenceSession session, params CollectionRecord[] records)
    {
        foreach (var record in records)
        {
            if (record.RecordType != CollectionRecordTypes.DeviceStateSnapshot)
                continue;
            var snap = record.Payload.Deserialize<DeviceStateSnapshotPayload>();
            if (snap is not null)
                session.ApplyDeviceStateIdle(snap);
        }
    }

    [Fact]
    public void HandlerLoop_IdleTrueThenFalse_SessionSnapshotCarriesAccumulatedIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);

        var tIdle = t0.AddMinutes(3);
        var tResume = t0.AddMinutes(8);
        ApplyRecords(session,
            DeviceState(tIdle, idleSeconds: 180, isIdle: true),
            DeviceState(tResume, idleSeconds: 0, isIdle: false));

        var snap = session.Snapshot(tResume);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(8), tResume - t0);
        Assert.Equal(TimeSpan.FromMinutes(5), snap.AccumulatedIdle); // started at tIdle-180s = t0
        Assert.Equal(TimeSpan.FromMinutes(3), snap.AccumulatedWork);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedBreak);
    }
}
```

- [ ] **Step 2: Run the new test**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~DeviceStateIdleHandlerTests" --nologo
```

Expected: PASS (uses Task 2/3 API). If it fails, fix ApplyDeviceStateIdle.

- [ ] **Step 3: Wire AgentWorker**

In `HandleMessageAsync` (`AgentWorker.cs` ~line 173), first line of the method body:

```csharp
_presenceSession.ObserveInbound(DateTimeOffset.UtcNow);
```

In `HandleCollectionSubmitAsync`, inside the `foreach (var record in payload.Records)` loop, after the `TryEnqueue` block, add DeviceState idle application. Keep a flag and broadcast after the ack so the tray can tick:

```csharp
        var accepted = 0;
        var idleChanged = false;
        foreach (var record in payload.Records)
        {
            if (record.RecordType is not (CollectionRecordTypes.ActivitySnapshot
                or CollectionRecordTypes.AppUsageSnapshot
                or CollectionRecordTypes.DeviceStateSnapshot
                or CollectionRecordTypes.Screenshot
                or CollectionRecordTypes.FacePhoto))
                continue;

            if (record.RecordType == CollectionRecordTypes.DeviceStateSnapshot)
            {
                try
                {
                    var device = record.Payload.Deserialize<DeviceStateSnapshotPayload>();
                    if (device is not null)
                        idleChanged |= _presenceSession.ApplyDeviceStateIdle(device);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "DeviceState payload could not be applied to presence idle");
                }
            }

            if (_activityBuffer.TryEnqueue(record))
                accepted++;
            else
                _logger.LogWarning("Activity buffer full — dropping eventId={EventId}", record.EventId);
        }
```

After the existing CollectionRecordAck `reply(...)`, add:

```csharp
        if (idleChanged)
        {
            try
            {
                await _pipeServer.BroadcastAsync(BuildStatusEnvelope(correlationId: null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast status after idle change");
            }
        }
```

Keep the existing accepted-types `or` list as-is (do not add types). Only insert the DeviceState idle branch inside that loop.

In `ExecuteClockOut`, pass idle into SQLite (signature changes in Task 6 — for now keep compiling by not changing `SaveSessionHistory` until Task 6). In this task only change the DeviceState + ObserveInbound + broadcast pieces.

In `EnqueueWorkSessionSync`, do **not** add `AccumulatedIdle` until Task 7 (payload type change).

- [ ] **Step 4: Build Service**

```powershell
dotnet build .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.Service/AgentWorker.cs tests/ONEVO.Agent.Service.Tests/Lifecycle/DeviceStateIdleHandlerTests.cs
git commit -m "feat: apply DeviceState idle snapshots to PresenceSession"
```

---

### Task 5: Sleep detection — `PowerModeIdleListener`

**Files:**
- Create: `tray_app_maui/ONEVO.Agent.Service/Lifecycle/ISystemPowerEvents.cs`
- Create: `tray_app_maui/ONEVO.Agent.Service/Lifecycle/SystemPowerEvents.cs`
- Create: `tray_app_maui/ONEVO.Agent.Service/Lifecycle/PowerModeIdleListener.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/Program.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/Lifecycle/PowerModeIdleListenerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

public sealed class PowerModeIdleListenerTests
{
    private sealed class FakePower : ISystemPowerEvents
    {
        public event EventHandler<PowerModeChangedEventArgs>? PowerModeChanged;
        public void Raise(PowerModes mode) =>
            PowerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));
    }

    private sealed class FakeBroadcaster : IIpcBroadcaster
    {
        public int Broadcasts { get; private set; }
        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
        {
            Broadcasts++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SuspendThenResume_OnActiveSession_AccumulatesIdle()
    {
        var session = new PresenceSession();
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        session.ClockIn(t0);

        var power = new FakePower();
        var broadcast = new FakeBroadcaster();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, broadcast, new AgentStateMachine());

        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);
        Assert.True(session.Snapshot(DateTimeOffset.UtcNow).IsIdle);

        await Task.Delay(50);
        power.Raise(PowerModes.Resume);
        var snap = session.Snapshot(DateTimeOffset.UtcNow);
        Assert.False(snap.IsIdle);
        Assert.True(snap.AccumulatedIdle > TimeSpan.Zero);
        Assert.True(broadcast.Broadcasts >= 2);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Suspend_WhileAlreadyIdle_DoesNotResetStart()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var idleStart = t0.AddMinutes(10);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, idleStart);

        var power = new FakePower();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, new FakeBroadcaster(), new AgentStateMachine());
        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);

        var snap = session.Snapshot(idleStart.AddMinutes(1));
        Assert.Equal(idleStart, snap.CurrentIdleStartedAt);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Suspend_WithNoActiveSession_IsNoOp()
    {
        var session = new PresenceSession();
        var power = new FakePower();
        var broadcast = new FakeBroadcaster();
        var sut = new PowerModeIdleListener(
            NullLogger<PowerModeIdleListener>.Instance, session, power, broadcast, new AgentStateMachine());
        await sut.StartAsync(CancellationToken.None);
        power.Raise(PowerModes.Suspend);
        power.Raise(PowerModes.Resume);
        Assert.Equal(0, broadcast.Broadcasts);
        await sut.StopAsync(CancellationToken.None);
    }
}
```

`IIpcBroadcaster` is `ONEVO.Agent.Service.IPC.IIpcBroadcaster` with `BroadcastAsync(IpcEnvelope, CancellationToken)`. `AgentStateMachine` has a public parameterless constructor.

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PowerModeIdleListenerTests" --nologo
```

Expected: FAIL — types not found.

- [ ] **Step 3: Implement the listener**

`ISystemPowerEvents.cs`:

```csharp
namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;

public interface ISystemPowerEvents
{
    event EventHandler<PowerModeChangedEventArgs>? PowerModeChanged;
}
```

`SystemPowerEvents.cs`:

```csharp
namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;

public sealed class SystemPowerEvents : ISystemPowerEvents
{
    public event EventHandler<PowerModeChangedEventArgs>? PowerModeChanged
    {
        add => SystemEvents.PowerModeChanged += value;
        remove => SystemEvents.PowerModeChanged -= value;
    }
}
```

`PowerModeIdleListener.cs`:

```csharp
namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class PowerModeIdleListener : IHostedService
{
    private readonly ILogger<PowerModeIdleListener> _logger;
    private readonly PresenceSession _session;
    private readonly ISystemPowerEvents _power;
    private readonly IIpcBroadcaster _broadcaster;
    private readonly AgentStateMachine _stateMachine;

    public PowerModeIdleListener(
        ILogger<PowerModeIdleListener> logger,
        PresenceSession session,
        ISystemPowerEvents power,
        IIpcBroadcaster broadcaster,
        AgentStateMachine stateMachine)
    {
        _logger = logger;
        _session = session;
        _power = power;
        _broadcaster = broadcaster;
        _stateMachine = stateMachine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _power.PowerModeChanged += OnPowerModeChanged;
        _logger.LogInformation("PowerModeIdleListener subscribed to PowerModeChanged");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _power.PowerModeChanged -= OnPowerModeChanged;
        return Task.CompletedTask;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            if (!_session.HasActiveSession)
                return;

            var now = DateTimeOffset.UtcNow;
            var changed = e.Mode switch
            {
                PowerModes.Suspend => _session.StartAutoPause(PauseReason.Idle, now),
                PowerModes.Resume => EndResume(now),
                _ => false
            };

            if (changed)
            {
                _logger.LogInformation("PowerMode {Mode} applied to presence idle", e.Mode);
                _ = BroadcastStatusAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PowerModeChanged handler failed Mode={Mode}", e.Mode);
        }
    }

    private bool EndResume(DateTimeOffset now)
    {
        var ended = _session.EndAutoPause(PauseReason.Idle, now);
        _session.ObserveInbound(now);
        return ended;
    }

    private async Task BroadcastStatusAsync()
    {
        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.StatusResponse,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    new StatusResponsePayload(
                        _stateMachine.CurrentState,
                        DateTimeOffset.UtcNow,
                        _session.Snapshot(DateTimeOffset.UtcNow)))
            };
            await _broadcaster.BroadcastAsync(envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status after power event");
        }
    }
}
```

Resume with no open idle (gap fallback already closed it, or the user was not idle) does not broadcast; `ObserveInbound` still stamps the watermark so the next Snapshot does not re-open the gap.

In `Program.cs` inside `ConfigureServices`, after `services.AddSingleton<PresenceSession>();`:

```csharp
        services.AddSingleton<ISystemPowerEvents, SystemPowerEvents>();
        services.AddHostedService<PowerModeIdleListener>();
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~PowerModeIdleListenerTests" --nologo
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.Service/Lifecycle/ISystemPowerEvents.cs ONEVO.Agent.Service/Lifecycle/SystemPowerEvents.cs ONEVO.Agent.Service/Lifecycle/PowerModeIdleListener.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/Lifecycle/PowerModeIdleListenerTests.cs
git commit -m "feat: map Windows suspend/resume to Idle pause"
```

---

### Task 6: SQLite `session_history` persists `AccumulatedIdle`

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Service/Buffer/ActivityRecordBuffer.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/AgentWorker.cs` (`SaveSessionHistory` call)
- Test: `tray_app_maui/tests/ONEVO.Agent.Service.Tests/ActivityRecordBufferTests.cs`

- [ ] **Step 1: Extend the existing persist test**

Change `SaveSessionHistory_persists_row` to pass idle and round-trip it. Add a query helper **only if** none exists — otherwise assert by reading SQLite in the test:

```csharp
    [Fact]
    public void SaveSessionHistory_persists_idle_seconds()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var cin = DateTimeOffset.Parse("2026-08-07T04:00:00Z");
        var cout = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        buffer.SaveSessionHistory(
            cin, cout,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(7),
            2,
            "09:00 AM – 06:00 PM",
            TimeSpan.FromMinutes(20));

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={buffer.DatabasePath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT accumulated_idle_sec FROM session_history LIMIT 1;";
        var idle = Convert.ToDouble(cmd.ExecuteScalar());
        Assert.Equal(1200, idle, 0.001);
    }
```

In-memory DBs use a `file:onevo_mem_...?mode=memory&cache=shared` URI — a second connection with `buffer.DatabasePath` should see the same cache=shared DB. If this test cannot see the row, assert via a new `ActivityRecordBuffer` method is overkill; instead add `internal double ReadLastIdleSecondsForTests()` behind `InternalsVisibleTo` (already granted). Prefer the second connection first; fall back to internals only if the shared-cache URI does not work.

Also add:

```csharp
    [Fact]
    public void InitializeSchema_AddsIdleColumnOnExistingDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-idle-{Guid.NewGuid():N}.db");
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.OpenSqlite(); // do not add this — use raw SQL below
        }
        finally { }
    }
```

Do **not** add that broken test. Use this instead:

```csharp
    [Fact]
    public void InitializeSchema_AddsIdleColumnOnLegacySessionHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-idle-{Guid.NewGuid():N}.db");
        try
        {
            SQLitePCL.Batteries_V2.Init();
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    CREATE TABLE session_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        clock_in_at TEXT,
                        clock_out_at TEXT,
                        accumulated_break_sec REAL NOT NULL DEFAULT 0,
                        accumulated_work_sec REAL NOT NULL DEFAULT 0,
                        break_session_count INTEGER NOT NULL DEFAULT 0,
                        schedule_display TEXT,
                        created_at TEXT NOT NULL
                    );
                    """;
                cmd.ExecuteNonQuery();
            }

            using var buffer = new ActivityRecordBuffer(path, maxRecords: 100);
            buffer.SaveSessionHistory(
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                TimeSpan.Zero, TimeSpan.Zero, 0, null, TimeSpan.FromSeconds(9));

            using var read = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            read.Open();
            using var q = read.CreateCommand();
            q.CommandText = "SELECT accumulated_idle_sec FROM session_history LIMIT 1;";
            Assert.Equal(9d, Convert.ToDouble(q.ExecuteScalar()), 0.001);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run tests — expect FAIL** (no param / no column)

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~ActivityRecordBufferTests" --nologo
```

- [ ] **Step 3: Implement schema + method**

In `InitializeSchema`, after the `CREATE TABLE IF NOT EXISTS session_history` block (same command or a follow-up command), add:

```csharp
            EnsureColumnUnlocked(
                "session_history",
                "accumulated_idle_sec",
                "REAL NOT NULL DEFAULT 0");
```

Add private method on `ActivityRecordBuffer`:

```csharp
    private void EnsureColumnUnlocked(string table, string column, string declaration)
    {
        using var info = _conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alter = _conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }
```

`table`/`column`/`declaration` are compile-time constants only — never concatenate user input.

Update `SaveSessionHistory` signature and INSERT:

```csharp
    public void SaveSessionHistory(
        DateTimeOffset? clockIn,
        DateTimeOffset? clockOut,
        TimeSpan accumulatedBreak,
        TimeSpan accumulatedWork,
        int breakSessionCount,
        string? scheduleDisplay,
        TimeSpan accumulatedIdle)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO session_history
                    (clock_in_at, clock_out_at, accumulated_break_sec, accumulated_work_sec,
                     break_session_count, schedule_display, created_at, accumulated_idle_sec)
                VALUES
                    ($cin, $cout, $brk, $work, $breaks, $sched, $created, $idle);
                """;
            cmd.Parameters.AddWithValue("$cin", (object?)clockIn?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cout", (object?)clockOut?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brk", accumulatedBreak.TotalSeconds);
            cmd.Parameters.AddWithValue("$work", accumulatedWork.TotalSeconds);
            cmd.Parameters.AddWithValue("$breaks", breakSessionCount);
            cmd.Parameters.AddWithValue("$sched", (object?)scheduleDisplay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$idle", accumulatedIdle.TotalSeconds);
            cmd.ExecuteNonQuery();
        }
    }
```

Update the existing `SaveSessionHistory_persists_row` call to pass `TimeSpan.Zero` as the last argument.

In `AgentWorker.ExecuteClockOut`:

```csharp
            _activityBuffer.SaveSessionHistory(
                snap.ClockInAt,
                snap.ClockOutAt,
                snap.AccumulatedBreak,
                snap.AccumulatedWork,
                snap.BreakSessionCount,
                snap.ScheduleDisplay,
                snap.AccumulatedIdle);
            _logger.LogInformation(
                "Session saved to SQLite Work={Work} Break={Break} Idle={Idle} Breaks={Count} Db={Db}",
                snap.AccumulatedWork, snap.AccumulatedBreak, snap.AccumulatedIdle, snap.BreakSessionCount,
                _activityBuffer.DatabasePath);
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~ActivityRecordBufferTests" --nologo
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.Service/Buffer/ActivityRecordBuffer.cs ONEVO.Agent.Service/AgentWorker.cs tests/ONEVO.Agent.Service.Tests/ActivityRecordBufferTests.cs
git commit -m "feat: persist AccumulatedIdle in SQLite session_history"
```

---

### Task 7: Work-session sync payload carries idle to the backend

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.Shared/Models/CollectionRecord.cs` (`WorkSessionPayload`)
- Modify: `tray_app_maui/ONEVO.Agent.Service/Api/ActivityIngestModels.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/Sync/ActivitySyncService.cs`
- Modify: `tray_app_maui/ONEVO.Agent.Service/AgentWorker.cs` (`EnqueueWorkSessionSync`)

Keep `CollectionSchemaVersions.WorkSessionV1 = "1.0"`. New property is optional so in-flight queued records without it deserialize as `TimeSpan.Zero`.

- [ ] **Step 1: Add `AccumulatedIdle` to `WorkSessionPayload`**

In `CollectionRecord.cs`, after `AccumulatedWork`:

```csharp
    public TimeSpan AccumulatedIdle { get; init; }
```

Not `required` — old buffered JSON remains valid.

- [ ] **Step 2: Add wire field**

In `ActivityIngestModels.cs` `WorkSessionSubmitRequest`, after `accumulated_work_seconds`:

```csharp
    [JsonPropertyName("accumulated_idle_seconds")]
    public int AccumulatedIdleSeconds { get; set; }
```

- [ ] **Step 3: Map it**

In `AgentWorker.EnqueueWorkSessionSync`, set:

```csharp
            AccumulatedIdle = snap.AccumulatedIdle,
```

on the `WorkSessionPayload`.

In `ActivitySyncService.FlushWorkSessionsAsync` (the `WorkSessionSubmitRequest` construction ~line 789):

```csharp
                AccumulatedIdleSeconds = (int)session.AccumulatedIdle.TotalSeconds,
```

- [ ] **Step 4: Build**

```powershell
dotnet build .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --nologo --filter "FullyQualifiedName~ActivitySyncServiceTests"
```

Expected: Build succeeded; existing sync tests still PASS (`AccumulatedIdle` defaults to zero).

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.Shared/Models/CollectionRecord.cs ONEVO.Agent.Service/Api/ActivityIngestModels.cs ONEVO.Agent.Service/Sync/ActivitySyncService.cs ONEVO.Agent.Service/AgentWorker.cs
git commit -m "feat: sync AccumulatedIdle with completed work sessions"
```

---

### Task 8: TrayApp active session — Idle Time line + work timer subtracts idle

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs`
- Modify: `tray_app_maui/ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml`
- Test: `tray_app_maui/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ApplySession_IdleOpen_WorkDurationExcludesIdle()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddMinutes(-10);
        var idleStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        vm.ApplySession(new SessionSnapshot(
            ClockInAt: clockIn,
            ClockOutAt: null,
            IsOnBreak: false,
            CurrentBreakStartedAt: null,
            AccumulatedBreak: TimeSpan.Zero,
            AccumulatedWork: TimeSpan.FromMinutes(8),
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: 0,
            AccumulatedIdle: TimeSpan.Zero,
            IsIdle: true,
            CurrentIdleStartedAt: idleStart));

        var workParts = vm.WorkDurationDisplay.Split(':');
        var workSecs = int.Parse(workParts[0]) * 3600 + int.Parse(workParts[1]) * 60 + int.Parse(workParts[2]);
        Assert.InRange(workSecs, 7 * 60 - 5, 8 * 60 + 5);

        var idleParts = vm.IdleTimeDisplay.Split(':');
        var idleSecs = int.Parse(idleParts[0]) * 3600 + int.Parse(idleParts[1]) * 60 + int.Parse(idleParts[2]);
        Assert.InRange(idleSecs, 110, 130);
    }

    [Fact]
    public void ApplySession_ClosedIdle_ShowsIdleAndReducedWork()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddHours(-1);
        vm.ApplySession(new SessionSnapshot(
            clockIn, null, false, null,
            TimeSpan.Zero, TimeSpan.FromMinutes(50),
            "09:00 AM – 06:00 PM", 0,
            AccumulatedIdle: TimeSpan.FromMinutes(10),
            IsIdle: false,
            CurrentIdleStartedAt: null));

        Assert.Equal("00:10:00", vm.IdleTimeDisplay);
        var workParts = vm.WorkDurationDisplay.Split(':');
        var workSecs = int.Parse(workParts[0]) * 3600 + int.Parse(workParts[1]) * 60 + int.Parse(workParts[2]);
        Assert.InRange(workSecs, 49 * 60, 51 * 60);
    }
```

Productive Time on this page currently equals work duration — keep that (work already excludes idle). Do not change header/status to "Idle"; the employee stays in Working chrome (spec: no idle prompt UX).

- [ ] **Step 2: Run tests — expect FAIL** (`IdleTimeDisplay` missing)

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~ActiveSessionViewModelTests" --nologo
```

- [ ] **Step 3: Update the view model**

Add fields next to the existing break fields:

```csharp
    private TimeSpan _accumulatedIdle;
    private DateTimeOffset? _currentIdleStartedAt;

    [ObservableProperty] private string _idleTimeDisplay = "00:00:00";
    [ObservableProperty] private bool _isIdle;
```

In `ApplySession` `Apply()`:

```csharp
            _accumulatedIdle = session.AccumulatedIdle < TimeSpan.Zero
                ? TimeSpan.Zero
                : session.AccumulatedIdle;
            _currentIdleStartedAt = NormalizeUtc(session.CurrentIdleStartedAt);
            IsIdle = session.IsIdle;
            if (IsIdle && _currentIdleStartedAt is null)
                _currentIdleStartedAt = DateTimeOffset.UtcNow;
```

In `UpdateTimersCore`, after computing `breakTotal`:

```csharp
        TimeSpan openIdle = TimeSpan.Zero;
        if (IsIdle)
        {
            if (_currentIdleStartedAt is null)
                _currentIdleStartedAt = now;
            openIdle = now - _currentIdleStartedAt.Value;
            if (openIdle < TimeSpan.Zero)
                openIdle = TimeSpan.Zero;
        }

        var idleTotal = _accumulatedIdle + openIdle;
        if (idleTotal < TimeSpan.Zero)
            idleTotal = TimeSpan.Zero;

        TimeSpan work = TimeSpan.Zero;
        if (_clockInAt is not null)
        {
            var wall = now - _clockInAt.Value;
            if (wall < TimeSpan.Zero)
                wall = TimeSpan.Zero;
            work = wall - breakTotal - idleTotal;
            if (work < TimeSpan.Zero)
                work = TimeSpan.Zero;
        }

        WorkDurationDisplay   = Format(work);
        BreakTimeDisplay      = Format(breakTotal);
        IdleTimeDisplay       = Format(idleTotal);
        ProductiveTimeDisplay = Format(work);
        PrimaryTimer = Format(IsOnBreak ? openBreak : work);
```

Remove the old `work = wall - breakTotal` block this replaces.

- [ ] **Step 4: Update `ActiveSessionPage.xaml` summary strip**

Replace the **Tasks Completed** column (`Grid.Column="3"`) with Idle Time:

```xml
              <VerticalStackLayout Grid.Column="3" Spacing="2">
                <Label Text="Idle Time" FontSize="10" TextColor="{StaticResource TextSecondary}" />
                <Label Text="{Binding IdleTimeDisplay}" FontSize="14" FontAttributes="Bold"
                       TextColor="{StaticResource TextPrimary}" />
              </VerticalStackLayout>
```

Leave `TasksCompletedDisplay` on the VM if other code references it; if nothing else binds it, delete the `[ObservableProperty]` to satisfy unused-member warnings (`TreatWarningsAsErrors` is on).

- [ ] **Step 5: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~ActiveSessionViewModelTests" --nologo
```

Expected: PASS. Existing work-timer tests still pass (no idle → same numbers).

- [ ] **Step 6: Commit**

```powershell
git add ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs
git commit -m "feat: show Idle Time on active session and exclude it from work"
```

---

### Task 9: End session summary uses `AccumulatedIdle` as source of truth

**Files:**
- Modify: `tray_app_maui/ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs`
- Test: `tray_app_maui/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/EndSessionViewModelTests.cs`

End page already has an Idle Time cell. Today it uses `SessionDayMetrics.TotalIdle` (60s DeviceState window approximation) and subtracts that from `AccumulatedWork`. After this feature, `AccumulatedWork` **already** excludes idle — subtracting day-metrics idle would double-count.

- [ ] **Step 1: Replace `LoadFromSnapshot_UsesDayMetricsForIdleAndTopApps` idle assertions**

Keep top-apps from day metrics. Drive idle from the snapshot:

```csharp
    [Fact]
    public void LoadFromSnapshot_UsesSnapshotIdle_NotDayMetrics()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        metrics.AddIdleSample(TimeSpan.FromMinutes(20));
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(45));
        metrics.AddAppUsageSample("Code.exe", TimeSpan.FromMinutes(90));

        var pipe = new FakeNamedPipeClient();
        var vm = new EndSessionViewModel(pipe, metrics);
        var clockIn  = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var clockOut = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockOut, false, null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(2) + TimeSpan.FromMinutes(30),
            null, 1,
            AccumulatedIdle: TimeSpan.FromMinutes(20)));

        Assert.Equal("00:20:00", vm.IdleTimeDisplay);
        Assert.Equal("02:30:00", vm.ProductiveTimeDisplay);
        Assert.Contains(vm.TopApps, a => a.Name.Contains("Code", StringComparison.OrdinalIgnoreCase));
    }
```

Update `LoadFromSnapshot_FormatsDisplays`: with zero idle, productive stays equal to working time (`08:10:00`).

Update `LoadSummary` to pass idle into the snapshot:

```csharp
        LoadFromSnapshot(new SessionSnapshot(
            ClockInAt: clockIn,
            ClockOutAt: clockOut,
            IsOnBreak: false,
            CurrentBreakStartedAt: null,
            AccumulatedBreak: breakTime,
            AccumulatedWork: (clockOut - clockIn) - breakTime - afkTime,
            ScheduleDisplay: null,
            BreakSessionCount: breakTime > TimeSpan.Zero ? 1 : 0,
            AccumulatedIdle: afkTime));
```

- [ ] **Step 2: Run tests — expect FAIL** on productive/idle math

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~EndSessionViewModelTests" --nologo
```

- [ ] **Step 3: Change `LoadFromSnapshot` idle/productive block**

Replace the `Productive ≈ work minus measured idle` block with:

```csharp
        var idle = session.AccumulatedIdle < TimeSpan.Zero
            ? TimeSpan.Zero
            : session.AccumulatedIdle;

        BreakTimeDisplay   = Format(breakTime);
        WorkingTimeDisplay = Format(workTime);
        IdleTimeDisplay    = Format(idle);
        ProductiveTimeDisplay = Format(workTime);
        BreakSessionsDisplay = Math.Max(0, session.BreakSessionCount).ToString();
```

If `workTime == TimeSpan.Zero` recovery from anchors still runs, also subtract idle:

```csharp
            workTime = wall - breakTime - idle;
            if (workTime < TimeSpan.Zero) workTime = TimeSpan.Zero;
```

Compute `idle` **before** that recovery. Total shift remains `clockOut - clockIn` (wall), not work+break (idle is inside the shift).

- [ ] **Step 4: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~EndSessionViewModelTests" --nologo
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/EndSessionViewModelTests.cs
git commit -m "fix: end-session Idle/Productive use AccumulatedIdle"
```

---

### Task 10: Backend — persist and report `AccumulatedIdleSeconds`

**Files (all under `HRMS-Backend-v1`):**
- Modify: `src/ONEVO.Domain/Features/Monitoring/WorkSessions/Entities/EmployeeWorkSession.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/WorkSessions/Commands/SubmitWorkSession/SubmitWorkSessionCommand.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/WorkSessions/Commands/SubmitWorkSession/SubmitWorkSessionCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/WorkSessions/Commands/SubmitWorkSession/SubmitWorkSessionCommandValidator.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Monitoring/WorkSessions/MonitoringWorkSessionsController.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/DailyReport/DTOs/Responses/EmployeeDailyReportDto.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/DailyReport/Queries/GetEmployeeDailyReport/GetEmployeeDailyReportQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/DailyReport/GetEmployeeDailyReportQueryHandlerTests.cs`
- Create via `dotnet ef`: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddWorkSessionAccumulatedIdle.cs`

No new RLS policy — column add on an existing tenant-owned table.

- [ ] **Step 1: Entity + command + validator + controller + handler mapping**

On `EmployeeWorkSession`, after `AccumulatedWorkSeconds`:

```csharp
    public int AccumulatedIdleSeconds { get; set; }
```

`SubmitWorkSessionCommand` — add `int AccumulatedIdleSeconds` before `BreakSessionCount`.

Validator:

```csharp
        RuleFor(x => x.AccumulatedIdleSeconds).GreaterThanOrEqualTo(0);
```

Handler create:

```csharp
            AccumulatedIdleSeconds = request.AccumulatedIdleSeconds,
```

Controller `SubmitWorkSessionRequest` — add with default so old agents work:

```csharp
    [property: JsonPropertyName("accumulated_idle_seconds")] int AccumulatedIdleSeconds = 0,
```

Pass `request.AccumulatedIdleSeconds` into the command.

`EmployeeDailyReportDto`:

```csharp
    public int IdleMinutes { get; init; }
```

Handler, next to `breakSeconds`:

```csharp
        var idleSeconds = workSessions.Sum(s => s.AccumulatedIdleSeconds);
```

And on the DTO:

```csharp
            IdleMinutes = idleSeconds / 60,
```

- [ ] **Step 2: Update daily-report unit test**

In the seeded `EmployeeWorkSession`, set `AccumulatedIdleSeconds = 600`. After handle:

```csharp
        report.IdleMinutes.Should().Be(10);
        report.WorkedMinutes.Should().Be(480);
        report.BreakMinutes.Should().Be(30);
```

(`AccumulatedWorkSeconds = 28800` already excludes idle in the agent-produced number; do not subtract again.)

- [ ] **Step 3: Run unit tests (expect FAIL until entity compiles)**

From `HRMS-Backend-v1`:

```powershell
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEmployeeDailyReportQueryHandlerTests" --nologo
```

- [ ] **Step 4: Migration**

From `HRMS-Backend-v1`:

```powershell
dotnet ef migrations add AddWorkSessionAccumulatedIdle --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api
```

Open the generated `Up` and confirm it is equivalent to:

```csharp
            migrationBuilder.AddColumn<int>(
                name: "accumulated_idle_seconds",
                table: "employee_work_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
```

Do not hand-write a second migration if `dotnet ef` already produced this.

- [ ] **Step 5: Re-run tests + build**

```powershell
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetEmployeeDailyReportQueryHandlerTests" --nologo
dotnet build .\src\ONEVO.Api\ONEVO.Api.csproj
```

Expected: PASS / Build succeeded.

- [ ] **Step 6: Commit (backend repo)**

```powershell
git add src/ONEVO.Domain/Features/Monitoring/WorkSessions/Entities/EmployeeWorkSession.cs src/ONEVO.Application/Features/Monitoring/WorkSessions src/ONEVO.Api/Controllers/Tenant/Monitoring/WorkSessions/MonitoringWorkSessionsController.cs src/ONEVO.Application/Features/Monitoring/DailyReport src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Unit/Features/Monitoring/DailyReport/GetEmployeeDailyReportQueryHandlerTests.cs
git commit -m "feat: persist and report work-session idle seconds"
```

Include the generated Designer + `ApplicationDbContextModelSnapshot.cs` in that add.

---

### Task 11: Full agent test pass + compile the TrayApp constructors

**Files:** any remaining `new SessionSnapshot(` sites that fail to compile without the new optional args (they should not — defaults were added). `_pipeprobe/Program.cs` compiles against Shared; rebuild it if the solution includes it.

- [ ] **Step 1: Run the agent test suite**

From `tray_app_maui`:

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --nologo
dotnet test .\tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj --nologo
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --nologo
```

Expected: all PASS.

- [ ] **Step 2: Fix any leftover compile breaks** (`FakeNamedPipeClient` is fine with defaults; `IpcEnvelopeTests` is fine). If `TreatWarningsAsErrors` flags unused `TasksCompletedDisplay`, remove it in this commit.

- [ ] **Step 3: Commit only if Step 2 produced diffs**

```powershell
git add -u
git commit -m "fix: compile and test leftovers for idle exclusion"
```

Skip the commit if `git status` is clean.

---

### Task 12: Manual verification — real suspend/resume (cannot be unit tested)

This is the spec's PowerMode spike. Do it on a machine (or VM) with the Service installed the same way production runs (`UseWindowsService`).

- [ ] **Step 1: Clock in on TrayApp. Confirm DeviceState is flowing (idle 0 while moving the mouse).**

- [ ] **Step 2: Leave the session Active (not on break). Suspend the machine for at least 3 minutes (Start → Power → Sleep, or `rundll32.exe powrprof.dll,SetSuspendState 0,1,0` if that is how this environment sleeps). Resume.**

- [ ] **Step 3: Clock out. On End Session, Idle Time must be within ~1 minute of the sleep duration (plus any pre-sleep input-idle). Work Duration must exclude that idle. Break Time must stay 0 if no manual break was taken.**

- [ ] **Step 4: Check Service logs for either `PowerMode Suspend applied to presence idle` (primary path) or a gap-fallback idle of similar length (no PowerMode line). Record which path fired in the PR/commit message of any follow-up fix.**

- [ ] **Step 5: Repeat once with a manual break open during sleep.** Break Time must include the break; Idle must **not** increase for the overlapping sleep (idle is a no-op while `ManualBreak` is open).

If PowerMode never fires and the gap fallback is the only path: that is acceptable for Phase 1 as long as Step 3 numbers are correct. Do not add TrayApp-side power IPC (spec: Service-only).

No commit required unless a bugfix is needed.

---

## Self-review (spec coverage)

| Spec requirement | Task |
|---|---|
| `AccumulatedWork = wall - break - idle` | 2 |
| `PauseReason` ManualBreak / Idle; sleep is a source of Idle, not its own reason | 2, 5 |
| Single open-pause slot; idle no-op during break | 2 |
| `StartAutoPause` / `EndAutoPause` idempotent, no start reset | 2 |
| `StartBreak`/`EndBreak` go through the same slot | 2 |
| `SessionSnapshot.AccumulatedIdle` (+ live-tick fields) | 1, 2 |
| DeviceState `IsIdle` → start/end with `IdleSeconds` back-date | 3, 4 |
| `PowerModeChanged` Suspend/Resume | 5, 12 |
| Gap fallback ~3× DeviceState interval | 3, 4 |
| No new IPC; no idle prompt UI; Windows-only; 120s threshold unchanged | 4, 8, 12 (non-goals honored) |
| SQLite persistence | 6 |
| Backend DTO/migration/reporting | 10 |
| Active session Idle vs Break lines | 8 |
| Unit tests listed in spec | 2, 3, 5 |
| AgentWorker DeviceState sequence / SessionSnapshot payload | 4 |
| Manual suspend/resume | 12 |
| Manual break flow unchanged (Paused state, collector stop) | 2, 4 (idle does not call `TryTransition`) |

Placeholder scan: no TBD/TODO left. Types (`PauseReason`, `StartAutoPause`, `ApplyDeviceStateIdle`, `ObserveInbound`, `AccumulatedIdleSeconds`, `IdleMinutes`) are named consistently across tasks.
