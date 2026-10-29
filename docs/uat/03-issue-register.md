# 03 — Issue Register — `develop`

Classification: **Verified** (reproduced by test/experiment), **Confirmed (code)** (follows from code, not executed), **Observation** (design limitation / decision needed).

## Findings

| ID | P | Component | Title | Class | Status |
|---|---|---|---|---|---|
| UAT-01 | P1 | Tray `ActivityCountCollector` | Activity snapshots over-report active time | Confirmed (code); fix unit-tested | **Fixed in PR #100** (`61e43f9`, `f987d83`) — device check pending |
| UAT-02 | P3 | Tray `SessionDayMetrics` | Idle accumulated twice (two collectors), value unused | Confirmed (code) | Open |
| UAT-03 | P3 | Tray logging / privacy | Per-snapshot keyboard/mouse counts written to `tray.log` | Confirmed (code) | Open — policy decision |
| UAT-04 | — | Service `PresenceSession` | Idle end detected at 60 s sample resolution | Observation | Accepted limitation (not changed) |
| UAT-05 | P2 | Release / git | GitHub default branch `main` is still `5cca93e` | Verified | Open — release process (AGT-03) |
| UAT-06 | P3 | Tests | ~135 Service tests fail off-Windows without being marked Windows-only | Verified | Open — informational |
| UAT-07 | **P1** | Packaging | Released MSIX does not contain or install the Agent Service | Verified (manifest, release.yml) | **Escalated** — installer decision |
| UAT-08 | **P1** | Packaging / lifecycle | Tray does not start at logon (no startup task / Run key) | Verified (manifest, code search) | **Escalated** — installer decision |
| UAT-09 | P3 | Security | Credential confidentiality relies on the folder ACL (DPAPI LocalMachine) | Confirmed (code) | Device verification (11 §F) |
| UAT-10 | Info | Release | Production code-signing certificate needed; dev cert requires manual trust | Verified (`release.yml`, `build-msix.ps1`) | T17 |
| UAT-11 | P3 | Configuration | Shipped `ApiBaseUrl` must be confirmed per environment | Verified (`appsettings.json`) | Open — backend team |

### UAT-01 — Activity snapshots over-report active time (P1)
- **File:** `ONEVO.Agent.TrayApp/Collectors/ActivityCountCollector.cs` (`EmitSnapshotAsync`, `SnapshotLoopAsync`, `StopAsync`).
- **Actual (before):**
  - Every snapshot reported `active_seconds + idle_seconds = 60`. That included the first one, emitted 5 s after start, and the final one emitted on stop.
  - The collector stops and restarts on break start/end, screen lock / user switch (AUD-014), IPC reconnect and policy changes. Each stop/start pair added up to ~110 s of phantom time to the backend `activity/snapshots` data.
  - Idle was read only at the snapshot instant (`secondsSinceInput >= interval/2`). 40 s idle followed by 20 s typing reported 60 s active, 0 idle.
- **Expected:** a snapshot covers exactly the time since the previous snapshot, and every idle span inside it is counted.
- **Fix:** `ActivityIntervalTracker` (pure, injected monotonic clock):
  - samples seconds-since-last-input every 5 s;
  - counts every no-input span ≥ half the interval (the unchanged idle rule) from its first second;
  - reports real elapsed time capped at the interval, so sleep/hibernate time is never reported as activity.
  - Intensity is computed over the covered time.
- **Scope:** the IPC/backend wire format is unchanged. Attendance work time is not affected; it comes from `PresenceSession`.
- **Tests:** `ActivityIntervalTrackerTests` (8; also compiled and run standalone on Linux: 8/8) and `ActivityCountCollectorAccountingTests` (4, headless).
- **Regression caught by CI and fixed:**
  - PR CI run 206 failed the existing interactive test `Hooks_CountInput_OnTheirOwnThread_WithoutTheCallerPumpingMessages`.
  - Cause: a stop within half a second of the last snapshot rounded to 0 covered seconds, and the first version of the fix then dropped the snapshot together with its input counts.
  - Fixed in `f987d83`: skip only when neither time nor input elapsed. That existing test is the regression test.
- **Remaining verification:** device check (manual checklist §B).

### UAT-07 — Agent Service is not shipped (P1)
- `Package.appxmanifest` declares only the tray application, with the protocol handler. `release.yml` builds and uploads only that MSIX.
- The Service is installed only by `scripts/install-service.ps1`, a developer script that publishes from a repo checkout with the SDK.
- A device that receives only the released MSIX has a tray that stays Offline.
- **Needs a packaging decision:** MSIX `desktop6:Service`, or an MSI/bootstrapper.

### UAT-08 — Tray does not start at logon (P1)
- There is no `windows.startupTask` in the manifest, and no Run key or scheduled task anywhere in the code or scripts.
- After a reboot during a workday, the Service receives no tray IPC. `PresenceSession`'s gap fallback then counts the time as idle until the employee opens the tray, and no activity is captured in the meantime.
- **Fix with UAT-07's installer work:** a startup task, or register the tray at install.

### UAT-11 — Environment endpoint (P3)
- The shipped `appsettings.json` targets `https://onexso.com:7229` for every environment unless overridden. Confirm the UAT and production values with the backend team.
- `PortalDevelopmentOrigins` / `PortalDevelopmentPorts` are honoured only when the host environment is Development (`PortalOriginValidator`), so they are not a production exposure.

### UAT-02 — Idle accumulated twice in tray day metrics (P3)
- `ActivityCountCollector` and `DeviceStateCollector` both call `ISessionDayMetrics.AddIdleSample` for the same idle time.
- `SessionDayMetrics.TotalIdle` has no production reader: the session idle shown to users comes from the Service (`SessionSnapshot.AccumulatedIdle`). There is therefore no visible impact today, but anyone who starts using `TotalIdle` gets roughly double the idle.
- **Recommendation:** keep a single source (DeviceState, which uses the policy idle threshold) or remove `TotalIdle`.

### UAT-03 — Activity counts in `tray.log` (P3, privacy)
- `ActivityCountCollector` logs `snapshot #N kb=… mouse=…` at Information. `TrayLogging` persists Information and above to the rolling `tray.log` (5 MB × retained files).
- These are counts only — no keys, coordinates or titles — but they form a persisted per-minute activity trail on the device, outside the backend's access controls.
- **Recommendation:** log at Debug, or drop the counts. Needs a monitoring-policy owner decision (assignment §7).

### UAT-04 — Idle end at sample resolution (observation)
- `PresenceSession.ApplyDeviceStateIdle` ends an idle pause at the DeviceState sample time (every 60 s), so each idle episode can include up to ~60 s that was actually worked.
- Using `CapturedAt − IdleSeconds` gains only seconds, because the user's last input is usually just before the sample. It would also change backend-visible attendance numbers and an existing test's expectation, so it was not changed.
- Shortening the DeviceState sample window is the effective lever if more precision is required.

### UAT-05 — Stale default branch (P2)
- `main` (the GitHub default branch) is `5cca93e`, 326 commits behind `develop`. Fresh clones and tools that use the default branch see a scaffold without most features.
- Already identified as AGT-03. The planned `develop → main` release PR has not happened yet.

### UAT-06 — Platform-specific tests not marked (P3)
- On Linux, 135 of 514 Service tests fail: `PlatformNotSupportedException` for DPAPI, ACL and SystemEvents, plus Windows path semantics.
- CI is Windows-only, so this is informational. Marking them Windows-only would let the platform-neutral majority run anywhere.

## Earlier findings (audit of `main`) → status on `develop`

| Earlier finding | `develop` status | Where |
|---|---|---|
| IPC handshake lost/deadlocked messages (shared reader, BOM) | Fixed | `NamedPipeAuthenticator` uses the connection's `BoundedLineReader`, BOM-less writers |
| IPC has no real client authentication | Fixed | `IIpcClientVerifier` (install path + console session) |
| Unbounded IPC line read | Fixed | `BoundedLineReader` |
| Records dropped on 401/403 or exceptions; corrupt payload lost | Fixed | AGT-01: peek/ack, per-record outcomes, quarantine keeps payload |
| Queue RAM-only | Fixed | SQLite queue, corrupt-DB preservation, capacity headroom (AGT-02) |
| Device credential readable by users | Fixed | AUD-003 |
| Tray never reconnects | Fixed | `NamedPipeClient` burst + steady-state retry |
| Collector start/stop race | Fixed | `CollectorCoordinator._reconcileLock` |
| LL hooks without a message loop | Fixed | Dedicated hook thread with `GetMessage` loop |
| Snapshot loop dies on IPC error | Fixed | Submit errors caught per snapshot |
| No single-instance guard | Fixed | AUD-006 |
| Pipe-server double dispose / dispose on exit | Fixed | `Interlocked.Exchange` disposal; AUD-006 safe double dispose |
| `ApiBaseUrl` http allowed | Fixed | `OptionsValidation` requires https |
| Unbounded stop flush | Fixed | `StopFlushTimeout` 10 s |
| HTTP responses not disposed | Fixed | AUD-015 |
| No persistent tray logs / correlation IDs | Fixed | AUD-018 |
| Login / enrollment / attendance absent | Implemented | Clock-in, breaks, face check, device pairing |
| Activity snapshot accounting | **Was still present → UAT-01** | This PR |
