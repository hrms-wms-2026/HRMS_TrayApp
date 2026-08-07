# Plan: Attendance Session UI Flow (Clock In → Break → Clock Out)

**Date:** 2026-08-06  
**Branch:** `feature/activity-count-collector`  
**Skill:** `onevo-maui-trayapp` + architecture rulebook  
**Goal:** Make the full employee workday flow match the mockups and actually work — Clock In starts a session, Working UI runs timers, Break pauses monitoring, End Break resumes, Clock Out shows daily summary.

---

## 1. Problem (why CLOCK IN “vela seiyala”)

| Layer | Current behavior | Required behavior |
|-------|------------------|-------------------|
| **ClockInViewModel** | Sends only `StatusRequest`; no navigation | Send lifecycle **ClockIn** command → Service → navigate to Working UI |
| **IPC contracts** | No ClockIn / Break / ClockOut message types | Add lifecycle command + result + richer status |
| **Service** | Handles Status / Collection / Activation only; no user lifecycle | Transition state machine + session timers + LifecycleGate |
| **ActiveSessionPage** | Bare placeholder stack (timer + 2 buttons) | Mock “You are now Clocked In!” / On Break layouts |
| **EndSessionPage** | Bare summary + stub commands | Mock “Workday Completed” summary |
| **Break UX** | Instant `StartBreak` with StatusRequest | Confirm dialog → Pause → On Break UI → End Break |
| **Navigation** | `EndWorkSession` / `ReturnToWork` empty comments | Shell routes driven by `MonitoringState` + session summary |

Architecture rule (§2.3, §5, §6): **Service owns the state machine**. Tray never fakes Active/Paused/Stopped locally. Collectors stop on break (Paused) and after clock-out (Stopped).

### Target journey (from mockups)

```text
[ClockInPage — Ready]
  mock: Jul 20 dashboard-ready (already close; polish only)
  action: CLOCK IN
        │
        ▼
[ActiveSessionPage — Working]          MonitoringState.Active
  mock: TRAY-CIN-02 “You are now Clocked In!”
  timers: Live Shift Timer, Today’s Summary
  actions: Break | Clock Out | Dashboard
        │
        ├─ Break → confirm dialog (TRAY-BRK-05)
        │           ▼
        │  [ActiveSessionPage — OnBreak]  MonitoringState.Paused
        │    mock: TRAY-BRK-06
        │    timer: Break Timer running; work timer paused
        │    actions: End Break → Active | Clock Out
        │
        └─ Clock Out
                  ▼
[EndSessionPage — Workday Completed]   MonitoringState.Stopped
  mock: TRAY-SUM-08
  summary: clock in/out, total shift, break, productive/idle stubs
  actions: View Dashboard | Download Summary (stub) | Close App
```

**Mock source files (copied under `docs/` for implementers):**
- `docs/mock-dashboard.png` — Ready / Clock In
- `docs/mock-working.png` — Clocked In / Working
- `docs/mock-break-modal.png` — Start Break?
- `docs/mock-on-break.png` — On Break
- `docs/mock-clockout.png` — Workday Completed

---

## 2. Architecture decisions

### 2.1 State mapping (non-negotiable)

| User action | Service `MonitoringState` | UI route | Collectors |
|-------------|---------------------------|----------|------------|
| Clock In (gates pass) | `Stopped` → `Active` | `//active` Working mode | Start |
| Start Break | `Active` → `Paused` | `//active` OnBreak mode | **Stop all** |
| End Break | `Paused` → `Active` | `//active` Working mode | Restart if gates pass |
| Clock Out | `Active|Paused` → `Stopped` | `//end` then later `//clockin` | Stop + flush best-effort |
| Unenrolled / Locked | as today | `//connect` | Off |

`App.xaml.cs` today maps `Paused` to default `//clockin` — **bug**. Must map `Paused` → `//active` (same page, OnBreak mode).

### 2.2 Session timing ownership

- **Service** is source of truth for: `ClockInAt`, `IsOnBreak`, `CurrentBreakStartedAt`, `AccumulatedBreak`, optional stub counters.
- **Tray** runs 1s UI timers for live display; on connect/status push it **resyncs** from Service (survives tray restart).
- Expand `StatusResponsePayload` (or add `SessionStatusPayload` on same StatusResponse) so UI always has enough data.

### 2.3 Out of scope for this plan (stub only)

| Feature | Mock shows | Phase behavior |
|---------|------------|----------------|
| **Dashboard** button | Yes | Open browser to configured portal URL **or** toast “Coming soon” — no in-app analytics dashboard |
| **Download Summary** | Yes | Stub: save simple text/JSON summary to Downloads **or** disabled with message |
| **Productive / Idle / Tasks / Top Apps** | Yes on summary | **Placeholder zeros / “—”** until collectors + backend feed real metrics (do not invent data) |
| **Backend T&A attendance API** | Implied | Local Service presence session only this PR; backend clock-in API can replace gate later |
| **Photo verify on clock-out** | Old EndSession had Verify Identity | Drop for mock-aligned summary (or keep optional secondary path later) |

### 2.4 Folder / layer rules (`onevo-maui-trayapp`)

- Shared contracts only in `ONEVO.Agent.Shared` — no platform code.
- Lifecycle transitions only in `ONEVO.Agent.Service` via `AgentStateMachine` + `LifecycleGate`.
- Tray ViewModels: CommunityToolkit.Mvvm, DI-registered, unit-tested with `FakeNamedPipeClient`.
- No Device JWT / secrets in Preferences or XAML.
- IPC loss → collectors stop (existing `CollectorCoordinator`); session UI shows offline/disconnected status.

---

## 3. Gap analysis (code today)

### Already good
- `ClockInPage.xaml` — two-panel Ready UI largely matches mock-dashboard (polish illustration/footer only).
- `AgentStateMachine` transitions: Stopped↔Active, Active↔Paused, →Stopped, Locked.
- `LifecycleGate` has `_presenceSessionActive` + `_notOnBreak`.
- Shell routes: `clockin`, `active`, `end` registered.
- Colors/styles from onboarding redesign reusable (`Card`, gradient button, status colors).

### Broken / missing
1. **No lifecycle IPC types** in `IpcMessages.cs`.
2. **ClockInCommand** does not request start monitoring or navigate.
3. **Service** never transitions on user action (only `ForceMonitoringActive` dev flag).
4. **Active/End pages** are scaffold UI not mock-aligned.
5. **No break confirmation** dialog.
6. **No End Break** command/path.
7. **Paused → wrong route** in `App.xaml.cs`.
8. **No session summary model** on StatusResponse.
9. **No ViewModel tests** for ActiveSession / EndSession.

---

## 4. Implementation design

### 4.1 Shared IPC (`ONEVO.Agent.Shared`)

Add to `IpcMessageTypes`:

```csharp
// Tray → Service
public const string LifecycleCommand = "LifecycleCommand";

// Service → Tray (reply)
public const string LifecycleResult  = "LifecycleResult";
```

Payloads:

```csharp
public enum LifecycleAction
{
    ClockIn,
    StartBreak,
    EndBreak,
    ClockOut
}

public sealed record LifecycleCommandPayload(
    LifecycleAction Action,
    string? BreakReason = null);  // optional; mock only needs confirm

public sealed record LifecycleResultPayload(
    bool Success,
    string? ErrorCode,            // e.g. INVALID_STATE, GATES_CLOSED, ALREADY_CLOCKED_IN
    string? Message,
    MonitoringState State,
    SessionSnapshot? Session);

public sealed record SessionSnapshot(
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    bool IsOnBreak,
    DateTimeOffset? CurrentBreakStartedAt,
    TimeSpan AccumulatedBreak,
    TimeSpan AccumulatedWork,     // wall work excluding breaks
    string? ScheduleDisplay,      // e.g. "09:00 AM – 06:00 PM" (config/default for now)
    int BreakSessionCount);

// Expand status so UI can resync without a separate poll type:
public sealed record StatusResponsePayload(
    MonitoringState State,
    DateTimeOffset Timestamp,
    SessionSnapshot? Session = null);
```

### 4.2 Service session + lifecycle handler

**New:** `ONEVO.Agent.Service/Lifecycle/PresenceSession.cs`  
In-memory session tracker (Phase 1; durable SQLite later if needed).

**Modify:** `AgentWorker.HandleMessageAsync`
- `LifecycleCommand` → validate current state → update `LifecycleGate` → `TryTransition` → update `PresenceSession` → reply `LifecycleResult` + push `StatusResponse` (so tray always updates).

Transition rules:

| Action | From | To | Gate side-effects |
|--------|------|-----|-------------------|
| ClockIn | Stopped | Active | `SetPresenceSessionActive(true)`, `SetNotOnBreak(true)`; other gates: use current + **dev defaults** if not enrolled (document: for enrolled path require CanActivate; for local demo allow option `AgentOptions.AllowLocalLifecycleWithoutFullGates`) |
| StartBreak | Active | Paused | `SetNotOnBreak(false)`; pause break timer start |
| EndBreak | Paused | Active | `SetNotOnBreak(true)`; accumulate break duration |
| ClockOut | Active/Paused | Stopped | presence false; finalize ClockOutAt; accumulate final break if needed |

**Dev pragmatism:** Full 9-gate enrollment is incomplete (ActivationCode handler is Phase 2 stub). Use:

```csharp
// appsettings.Development.json
"Agent": {
  "AllowLocalLifecycleWithoutFullGates": true
}
```

When true: ClockIn only requires current state `Stopped` (and not Locked). When false: require `LifecycleGate.CanActivate` after setting presence.

Push state to tray the same way StatusResponse already does so `App.xaml.cs` `OnStateReceived` navigates correctly.

### 4.3 Tray navigation model

**`App.xaml.cs` route map fix:**

```csharp
MonitoringState.Active     => "//active",
MonitoringState.Paused     => "//active",  // OnBreak mode
MonitoringState.Stopped    => lastSessionCompleted ? "//end" : "//clockin",
...
```

**Session completion nuance:** After ClockOut, show End page once, then user Close App → hide to tray; next open Ready clock-in. Implement with a small tray-side `ISessionUiState` (or query SessionSnapshot.ClockOutAt != null && State==Stopped → `//end`).

Prefer: **ClockOut LifecycleResult success → navigate `//end` with summary query/params**, and state push stays Stopped. Returning later (tray reopen) if `ClockOutAt` for “today” is set and no new clock-in → still End or Ready? Mock expects End after clock-out then Close. **Decision:** After ClockOut go `//end`; Close App hides window (existing); next explicit open of window from tray if same day clocked-out → show End or Ready with “already completed” — **show Ready (clockin) with optional banner** for simplicity, End only immediately after clock-out.

### 4.4 ViewModels

#### ClockInViewModel
- `ClockInCommand` sends `LifecycleCommand(ClockIn)`.
- On success: rely on state push → `//active` (or explicit `GoToAsync("//active")` after result).
- On error: set `ErrorMessage` from `ErrorCode` (ALREADY_CLOCKED_IN, GATES_CLOSED, etc.).
- Keep greeting/date/status cards; optional live clock timer for Current Time.

#### ActiveSessionViewModel (major expand)
Modes via `IsOnBreak` (bound to SessionSnapshot / local after LifecycleResult):

| Property | Working | On Break |
|----------|---------|----------|
| Title/header | “You are now Clocked In!” | “You are now On Break” |
| StatusText | Working (green) | On Break (orange) |
| PrimaryTimer | Live Shift / Work Duration | Break Timer |
| Secondary cards | Start Time, Today’s Schedule | same |
| Commands | `RequestBreakCommand`, `ClockOutCommand`, `OpenDashboardCommand` | `EndBreakCommand`, `ClockOutCommand`, `OpenDashboardCommand` |
| Today’s Summary | Work / Break / Productive(stub) / Tasks(stub) | same, break increasing |

Local 1s timer:
- If Working: `WorkDuration = now - ClockInAt - AccumulatedBreak` (pause during break).
- If OnBreak: `BreakLive = AccumulatedBreak + (now - CurrentBreakStartedAt)`.

`RequestBreakCommand` → show confirmation (not immediate IPC).  
`ConfirmStartBreakCommand` → `LifecycleCommand(StartBreak)`.  
`EndBreakCommand` → `LifecycleCommand(EndBreak)`.  
`ClockOutCommand` → `LifecycleCommand(ClockOut)` then navigate End with summary.

**Break confirmation UI:** Prefer MAUI overlay `Grid` on `ActiveSessionPage` (`IsBreakConfirmVisible`) matching mock modal — no new route. Alternative: `DisplayAlert` (faster, less pretty). **Recommend overlay** for mock fidelity.

#### EndSessionViewModel
Align to Workday Completed mock:
- Header, illustration placeholder, Status Clocked Out, Clock Out Time, Total Shift.
- Rows: Clock In, Clock Out, Total Work Duration, Total Break Time.
- Stubs: Productive Time, Idle Time, Break Sessions count, Top Applications (empty list message).
- Commands: `OpenDashboardCommand` (stub), `DownloadSummaryCommand` (optional simple file), `CloseAppCommand` (hide window / RequestExit false — hide only per existing tray behavior).

Remove or de-prioritize old “Verify Identity / Return to Work / Confirm Clock-Out” triple unless needed; mock is post-clock-out final summary (**clock-out already confirmed** when landing here).

### 4.5 Views (XAML redesign)

| Page | Work |
|------|------|
| `ClockInPage.xaml` | Minor polish to match mock-dashboard (footer Settings/version, illustration quality) — **optional low priority** |
| `ActiveSessionPage.xaml` | Full redesign: header, hero illustration area (emoji/placeholder OK), status+timer cards, action row (orange Break, red Clock Out, outline Dashboard), bottom Today’s Summary strip, break confirm overlay |
| `EndSessionPage.xaml` | Full redesign: Workday Completed, metric cards, action row, connection footer |

Reuse `Card` style, brand colors (`StatusOrange` for break, `StatusRed` for clock out, gradient primary). Window size may need **~1000×720** for summary density — adjust in `App.xaml.cs` if cramped.

### 4.6 NamedPipeClient / INamedPipeClient

Add helper:

```csharp
Task<LifecycleResultPayload?> SendLifecycleAsync(LifecycleAction action, CancellationToken ct);
```

Or keep thin: ViewModels build envelopes; client remains transport-only. Prefer thin transport + shared serialization helpers if needed.

Ensure `StatusResponse` deserialization updates a shared event:

```csharp
event Action<StatusResponsePayload>? OnStatusReceived; // richer than OnStateReceived
```

Keep `OnStateReceived` for backward compat by invoking both.

### 4.7 Collector impact

No new collectors. Existing `CollectorCoordinator` already reacts to Active/Paused/Stopped via pipe state events — verify Paused stops collectors (tests already mention this). After Service StartBreak → Paused push, collectors must stop within existing fail-safe path.

---

## 5. Task breakdown (implementation order)

### Task 1 — Shared contracts
- Add `LifecycleAction`, payloads, expand `StatusResponsePayload`, message type constants.
- Unit tests: serialize/deserialize round-trip (Shared.Tests).

### Task 2 — Service PresenceSession + lifecycle handler
- `PresenceSession.cs` + wire into `AgentWorker`.
- `AgentOptions.AllowLocalLifecycleWithoutFullGates`.
- State machine transitions + LifecycleGate updates.
- Service.Tests: ClockIn/StartBreak/EndBreak/ClockOut happy path + invalid state errors.

### Task 3 — Pipe client status enrichment
- Parse Session on StatusResponse; expose `OnStatusReceived`.
- `App.xaml.cs`: Paused → `//active`; optional session-aware End routing.

### Task 4 — ClockInViewModel wiring + tests
- Real `LifecycleCommand(ClockIn)`; error mapping; test with FakeNamedPipeClient returning result.

### Task 5 — ActiveSessionViewModel + tests
- Working/OnBreak modes, timers, break confirm flags, EndBreak, ClockOut.
- Unit tests for timer math (break accumulation), mode flags, command enablement.

### Task 6 — ActiveSessionPage XAML redesign + break overlay
- Match mock-working + mock-on-break + modal.
- Bind all new properties; no code-behind logic beyond DI BindingContext.

### Task 7 — EndSessionViewModel + EndSessionPage redesign
- Load from SessionSnapshot after ClockOut.
- Stub dashboard/download; Close hides window.
- Tests for `LoadSummary` / display formatting.

### Task 8 — Manual verification checklist
1. Run Service + Tray (dev force gates as needed).
2. Ready → CLOCK IN → Working UI + timer ticks.
3. Break → modal → confirm → On Break + break timer; collectors idle.
4. End Break → Working; work timer resumes.
5. Clock Out → Workday Completed summary.
6. Close → tray; reopen → Ready (or documented behavior).
7. Disconnect pipe during Working → fail-safe stop + UI status.

### Task 9 — Docs
- Short note in `docs/superpowers/plans/2026-08-06-attendance-session-ui.md` (this plan’s durable copy) with mock paths.

---

## 6. File map

### Create
- `ONEVO.Agent.Service/Lifecycle/PresenceSession.cs`
- `tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs` (or lifecycle handler tests)
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/EndSessionViewModelTests.cs`
- `docs/superpowers/plans/2026-08-06-attendance-session-ui.md` (copy of approved plan)

### Modify
- `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- `ONEVO.Agent.Service/AgentWorker.cs`
- `ONEVO.Agent.Service/Configuration/AgentOptions.cs` (+ appsettings*)
- `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs` / `NamedPipeClient.cs`
- `ONEVO.Agent.TrayApp/App.xaml.cs` (routing)
- `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- `ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs`
- `ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs`
- `ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml` (+ .cs if needed)
- `ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml`
- `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml` (optional polish)
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs` (lifecycle reply support)
- Possibly `tests/ONEVO.Agent.Shared.Tests/*` for new payloads

---

## 7. Testing strategy

| Layer | What |
|-------|------|
| Shared | JSON payload round-trip |
| Service | State transitions + session timer accumulation + invalid actions |
| Tray VM | Commands send correct envelopes; timer math with frozen clock if practical; IsOnBreak toggles |
| Manual | Full UI flow vs mock screenshots |

Do **not** claim done until `dotnet test` on Shared + Service + TrayApp tests pass and manual checklist §5 Task 8 is green.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| Gates block ClockIn before enrollment complete | Dev flag `AllowLocalLifecycleWithoutFullGates`; production path requires full gate |
| Tray restart mid-session loses UI | Resync SessionSnapshot from StatusResponse |
| Double CLOCK IN | Service rejects if not Stopped → ALREADY_CLOCKED_IN |
| Paused mis-routed to clockin | Explicit route map fix |
| Over-scoping Dashboard/metrics | Hard stubs only; no fake productivity numbers |
| XAML complexity | One page two modes + overlay; reuse Card styles |

---

## 9. Success criteria

1. From Ready screen, CLOCK IN lands on mock-like Working UI within ~1s and live timer moves.
2. Break confirmation appears; after confirm, UI shows On Break and Service is `Paused`.
3. End Break returns Working; break time accumulates correctly.
4. Clock Out shows Workday Completed with correct clock-in/out and break totals.
5. Collectors do not run while Paused/Stopped (existing coordinator + service reject path).
6. Unit tests cover lifecycle VM + service transitions.
7. No architecture violations: no JWT in UI, no tray-side state machine authority, no independent uploads.

---

## 10. Recommended execution approach

**Subagent-driven or sequential tasks 1→8** in order (contracts before Service before UI). Do not start ActiveSession XAML before IPC + Service ClockIn path works — otherwise UI still “vela seiyathu”.

**Estimate:** Medium feature — roughly 1 focused day for contracts+Service+wiring, 1 day for polished Active+End XAML + tests, plus manual pass.
