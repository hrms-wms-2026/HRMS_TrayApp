# Idle Time Exclusion (Input-Idle + Sleep) from AccumulatedWork

## Problem

`PresenceSession.Snapshot()` (`ONEVO.Agent.Service/Lifecycle/PresenceSession.cs:120-157`) computes the
payroll-relevant `AccumulatedWork` as pure wall-clock time minus explicit manual breaks:

```
AccumulatedWork = (now|ClockOutAt - ClockInAt) - AccumulatedBreak
```

Two categories of "not actually working" time are currently counted as active work:

1. **Input-idle time** — no keyboard/mouse input (`GetLastInputInfo`, 120s threshold in
   `ONEVO.Agent.TrayApp/Collectors/IdleDetector.cs:7`). Already detected and reported to the backend via
   `DeviceStateSnapshot` records, but only for dashboards — never fed back into work-duration math.
2. **Sleep time** — the machine is suspended/hibernated. No detection exists at all today. If a laptop
   sleeps for 2 hours while clocked in (not on an explicit break), those 2 hours count fully as work.

## Goals

- Exclude both input-idle time and sleep time from `AccumulatedWork`, same as manual breaks.
- Treat sleep as another *source* of idle time rather than its own category: both roll up into a single
  **Idle** bucket, distinct only from manual **Break**. Reporting shows two buckets: Break and Idle.
- No behavior change to existing manual break flow.

## Non-goals

- Changing the 120s idle threshold.
- Any TrayApp UI changes to prompt/warn the user about idle or sleep (future consideration, not in scope).
- macOS/mobile — Phase 1 is Windows-only per project scope.

## Design

### 1. `PresenceSession` model change

Add a `PauseReason` enum: `ManualBreak`, `Idle`. (Sleep is a detection *source* for `Idle`, not a separate
reason — see section 3.)

Add an `AccumulatedIdle` field alongside the existing `AccumulatedBreak`. Update the work-duration formula:

```
AccumulatedWork = wallClock - AccumulatedBreak - AccumulatedIdle
```

Internally, generalize break/idle tracking to share one "currently open pause" mechanism (a single
`(PauseReason Reason, DateTimeOffset StartedAt)?` slot) rather than independent parallel fields, to prevent
overlapping-pause bugs — e.g. idle detection must be a no-op while a manual break is already open, since
that time is already excluded.

New API on `PresenceSession`:

- `StartAutoPause(PauseReason reason, DateTimeOffset startedAt)` — idempotent no-op (with a logged warning)
  if a pause of the same reason is already open.
- `EndAutoPause(PauseReason reason, DateTimeOffset endedAt)` — closes the open pause and adds
  `endedAt - startedAt` to the matching accumulator; no-op if nothing open, or if the open pause has a
  different reason (defensive — reasons should never cross without an intervening End).

Existing `StartBreak`/`EndBreak` (`PresenceSession.cs:47-76`) are refactored to go through the same
open-pause slot with `PauseReason.ManualBreak`, preserving current behavior.

`SessionSnapshot` (`ONEVO.Agent.Shared/IPC/IpcMessages.cs:79-87`) gains one new `AccumulatedIdle` field so
the TrayApp UI and backend sync can surface it distinctly from `AccumulatedBreak`.

### 2. Input-idle detection — reuse existing data flow, no new IPC

`DeviceStateCollector` already ticks every ~60s and sends `IsIdle`/`IdleSeconds` to the Service via
`CollectionRecordSubmit` IPC messages, handled in `AgentWorker.cs` (~line 590-599) and currently just
enqueued to the SQLite activity buffer.

Extend that same handler: when a `DeviceStateSnapshot` record arrives,

- if `record.IsIdle` and no `Idle` pause is currently open →
  `PresenceSession.StartAutoPause(Idle, startedAt: record.CapturedAt - TimeSpan.FromSeconds(record.IdleSeconds))`
- if `!record.IsIdle` and an `Idle` pause is open →
  `PresenceSession.EndAutoPause(Idle, endedAt: record.CapturedAt)`

Using `IdleSeconds` to back-date the pause start gives second-level accuracy on when idling began, even
though it's only detected on the next ~60s tick. Resume detection is bounded by the same tick interval.

### 3. Sleep detection — Service-only, feeds the same Idle bucket

The Service subscribes to `Microsoft.Win32.SystemEvents.PowerModeChanged` at startup.

- `PowerModes.Suspend` → for any currently active (clocked-in) session, `StartAutoPause(Idle, startedAt: now)`
  — a no-op if an `Idle` pause is already open (e.g. the user was already idle before the machine slept,
  which is the common case since the machine typically sleeps *because* of inactivity).
- `PowerModes.Resume` → `EndAutoPause(Idle, endedAt: now)`.

Because sleep and input-idle share the same `PauseReason.Idle` and the same open-pause slot, there's no
double counting when both signals fire close together (e.g. input goes idle, then the machine sleeps a
minute later, then a `DeviceStateSnapshot` after resume also reports `IsIdle`) — whichever signal opens the
pause first wins, and whichever closes it second is what actually ends it.

This runs entirely inside the Service process — no IPC, no TrayApp dependency, and it still works if
TrayApp isn't running when the machine sleeps or wakes.

**Validation spike required before implementation is trusted:** confirm `SystemEvents.PowerModeChanged`
actually fires reliably under this Service's hosting model (`.UseWindowsService()` / Generic Host). It
internally creates its own message-only window/thread so it's expected to work without a UI thread, but
this must be verified against a real suspend/resume cycle on this specific setup before relying on it as
the primary mechanism.

### 4. Fallback gap detection (safety net)

Track `LastKnownActivityAt` on the Service side — updated on every inbound IPC message from TrayApp (or
any Service tick). Whenever `Snapshot()` runs, if `now - LastKnownActivityAt` exceeds a threshold (e.g. 3x
the expected DeviceState tick interval, ~3 minutes) and no pause is currently open, retroactively open (and
immediately close) an `Idle`-reasoned pause spanning that gap.

This catches cases where the `PowerModeChanged` Suspend/Resume pair was missed entirely (e.g. Service was
itself restarting, or the event didn't fire for some reason) — it is a correctness backstop, not the
primary detection path.

## Data flow summary

```
Input-idle: TrayApp DeviceStateCollector (60s tick) --IPC--> AgentWorker --> PresenceSession.Start/EndAutoPause(Idle)
Sleep:      OS power event --SystemEvents--> Service (in-process)         --> PresenceSession.Start/EndAutoPause(Idle)
Fallback:   Snapshot() gap-check                                          --> PresenceSession.Start+EndAutoPause(Idle) retroactively
```

All three paths feed the same `AccumulatedIdle` accumulator through the same open-pause slot.

## Testing

- **Unit (`PresenceSession`)**: input-idle start/end accumulates correctly; sleep start/end accumulates
  into the same `AccumulatedIdle`; idle detection (either source) is a no-op while a manual break is open;
  duplicate/out-of-order `IsIdle=true` or `Suspend` signals don't reset an already-open idle pause;
  `Snapshot()` math is correct with combinations of break/idle; gap-detection fallback opens and closes a
  retroactive pause correctly.
- **Integration**: simulate `AgentWorker` receiving a sequence of `DeviceStateSnapshot` records crossing
  the idle threshold in both directions; verify `SessionSnapshot` IPC payload carries the right
  `AccumulatedIdle` value.
- **Manual verification (cannot be unit tested)**: real clock-in, physically suspend/resume the machine
  (or VM equivalent), confirm `AccumulatedIdle` reflects the actual sleep duration and `AccumulatedWork`
  excludes it. This is the validation spike from section 3 and should happen early, before the rest of the
  implementation is trusted.

## Open items carried into the implementation plan

- Exact SQLite/EF schema changes needed for `AccumulatedIdle` persistence and any backend DTOs/migrations
  on the HRMS-Backend side for reporting.
- TrayApp UI changes to display Idle/Break as separate lines in the active session view
  (`ActiveSessionPage.xaml`) — scope and copy to be defined in the plan.
