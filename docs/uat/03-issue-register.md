# 03 — Issue Register

Baseline revision: `5cca93e`. Last updated after `ef81a0c` (Windows CI run 203: 89/89 tests passed). Classification legend:

- **Verified** — reproduced by an automated test, experiment or build log.
- **Confirmed (code)** — follows deterministically from the code/platform documentation; not executed (usually needs Windows).
- **Suspected** — plausible, not proven.

Status values: Open · Escalated (needs human/backend decision) · Fixed – verified · Fixed – pending Windows verification · Won't fix.

## Summary

| ID | P | Component | Title | Class | Status |
|---|---|---|---|---|---|
| ISS-001 | P0 | Service + Tray | End-to-end activity pipeline not wired | Verified (code) | Escalated |
| ISS-002 | P0 | Auth | No login / enrollment / token lifecycle | Verified (code) | Escalated |
| ISS-003 | P0 | Attendance | Clock-in/out and break tracking not implemented | Verified (code) | Escalated |
| ISS-004 | P0 | Service buffer | Offline queue is RAM-only; all pending activity lost on restart | Confirmed (code) | Escalated |
| ISS-005 | P0 | Tray build | TrayApp project compiles nothing (no executable) | Verified (Windows CI) | Escalated |
| ISS-006 | **P0** | IPC | Handshake deadlocks on Windows / drops post-handshake message | Verified (Windows CI) | Fixed – verified |
| ISS-007 | P1 | Sync | Records permanently dropped on HTTP 401/403 | Verified | Fixed – verified |
| ISS-008 | P1 | Sync | Dequeued batch lost on unexpected exception; silent drops | Verified | Fixed – verified |
| ISS-009 | P1 | API contract | No idempotency key → duplicates after lost responses | Confirmed (code) | Escalated |
| ISS-010 | P1 | Security | Device JWT readable by any local user | Confirmed (code) | Fixed – verified (Windows CI) |
| ISS-011 | P1 | Security/IPC | IPC has no real client/server authentication | Confirmed (code) | Escalated |
| ISS-012 | P1 | Tray collector | LL hooks installed on a thread without a message loop | Confirmed (code) | Open – needs Windows device |
| ISS-013 | P1 | Tray IPC | Pipe client never reconnects after disconnect / 5 failed attempts | Verified | Fixed – verified |
| ISS-014 | P1 | Calculation | Active/idle seconds wrong for partial intervals; idle heuristic | Confirmed (code) | Fixed – unit-verified, device verification pending |
| ISS-015 | P1 | Tray collector | Start/stop race can orphan the snapshot loop (capture during break) | Verified | Fixed – verified |
| ISS-016 | P1 | Tray lifecycle | No single-instance guard → double counting | Confirmed (code) | Open – blocked by ISS-005 |
| ISS-017 | P1 | Tray collector | Snapshot loop dies permanently on first IPC write error | Confirmed (code) | Fixed – verified |
| ISS-018 | P2 | Service IPC | Pipe server: double-dispose exception, silent accept-loop death, unobserved auth faults | Verified | Fixed – verified |
| ISS-019 | P2 | Config/TLS | `ApiBaseUrl` not validated (http allowed); timeout option unused | Confirmed (code) | Partly fixed (https enforced; timeout open with ISS-001) |
| ISS-020 | P2 | IPC | Unbounded line read before size check | Confirmed (code) | Open |
| ISS-021 | P2 | Sync | Final flush on shutdown has no time bound | Verified | Fixed – verified |
| ISS-022 | P2 | Isolation | Multi-user machines: records carry no user identity | Confirmed (code) | Escalated |
| ISS-023 | P2 | Data integrity | Tray stamps `DeviceId = MachineName` | Confirmed (code) | Open |
| ISS-024 | P2 | Storage | Non-atomic credential/identity writes | Confirmed (code) | Fixed – verified |
| ISS-025 | P2 | Observability | No persistent tray logs, rotation, crash reporting, correlation IDs | Confirmed (code) | Open |
| ISS-026 | P2 | Tray UX | State never pushed; "Ready" shown while disconnected | Confirmed (code) | Open |
| ISS-027 | P3 | Repo | 363 build outputs committed; `.gitignore` deleted | Verified | Fixed |
| ISS-028 | P3 | Release | No CI, installer, or version stamping | Verified | Partly fixed (CI + CI version stamping; no installer) |
| ISS-029 | P3 | Dependencies | Runtime packages behind servicing; ProtectedData major mismatch | Verified | Fixed |
| ISS-030 | P3 | Buffer | Cap non-atomic, counts records not MB; requeue reorders | Verified (ordering) | Fixed (ordering, atomic cap); MB cap open |
| ISS-031 | P3 | State machine | `Locked→Locked` allowed; state not persisted | Verified (code) | Open |
| ISS-032 | P3 | Tray UI | Blocking init, dropped early state, empty MAUI window | Suspected | Open |
| ISS-033 | P3 | Sync | `HttpResponseMessage` never disposed | Confirmed (code) | Fixed |
| ISS-034 | P3 | Policy | Employee can exit tray and stop capture | Policy question | Escalated |
| ISS-035 | P2 | Tray lifecycle | Pipe-client disposal throws on exit (async-void handler → crash) | Verified | Fixed – verified |

---

## Details

### ISS-001 — End-to-end activity pipeline not wired (P0)
- **Component:** Service DI, AgentWorker, TrayApp DI
- **Files:** `ONEVO.Agent.Service/Program.cs:9-23`, `AgentWorker.cs:32-45`, `ONEVO.Agent.TrayApp/MauiProgram.cs:13-19`, `Configuration/AgentOptions.cs:21`
- **Problem:** `ActivityRecordBuffer`, `ActivitySyncService`, `PolicyCache` and the `"OnevoApi"` HttpClient are never registered; `AgentWorker` handles only `StatusRequest` (all `CollectionRecordSubmit` messages are ignored); no `PolicyPush` is ever sent; nothing transitions the state machine out of `Unenrolled`; `ForceMonitoringActive` is unused; the tray never registers `CollectorCoordinator`/`ActivityCountCollector`.
- **Actual:** No activity is captured or uploaded under any configuration.
- **Expected:** Tray captures → service buffers → service uploads.
- **Evidence:** Code inspection (grep for registrations returns none).
- **Root cause:** Scaffold not completed.
- **Recommended fix:** Product/architecture work: enrollment → state transitions → policy push → record ingestion in `AgentWorker` → registration of buffer/sync with a configured `HttpClient` (BaseAddress, timeout). Not a defect fix; requires design approval (rule 2 "do not invent features").
- **Risk/deps:** Depends on ISS-002, ISS-004, backend contract.
- **Test added:** none (feature work). **Verification:** n/a. **Status:** Escalated.

### ISS-002 — No login / enrollment / token lifecycle (P0)
- **Files:** `Views/LoginWindow.xaml` (title label only), `Security/CredentialStore.cs` (no caller of `StoreDeviceJwt`), `Security/DeviceIdentityStore.cs` (no caller of `Save`)
- **Actual:** No way to obtain, refresh or revoke a device token. **Expected:** enrollment flow and token expiry/refresh handling.
- **Recommended fix:** Define enrollment API contract with backend team; implement. **Status:** Escalated (backend contract needed).

### ISS-003 — Clock-in/out and break tracking not implemented (P0)
- **Files:** whole solution — no attendance types, IPC messages, API routes or UI. `MonitoringState.Paused` is labelled "On Break" in `TrayIconService.cs:55` but nothing sets it.
- **Impact:** T06 (attendance correctness) and T15 (login → attendance → break E2E) cannot be executed against this codebase.
- **Status:** Escalated — scope mismatch between assignment and implementation must be resolved by product owner.

### ISS-004 — Offline queue is RAM-only (P0)
- **Files:** `Buffer/ActivityRecordBuffer.cs:6-12` ("Phase-1 in-memory buffer until encrypted SQLite lands")
- **Actual:** Service restart, crash, reboot, upgrade or power loss discards every un-uploaded record (up to 5,000 ≈ 3.5 days at 1/min). **Expected:** durable queue surviving restarts (T05).
- **Recommended fix:** Encrypted SQLite (as planned) or append-only journal; needs a dependency decision (rule 4). **Status:** Escalated.

### ISS-005 — TrayApp does not build (P0) — verified, see update below
- **Files:** `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`, `Services/TrayIconService.cs:50`, `App.xaml.cs:5`
- **Evidence:** committed `obj/project.assets.json` has no MAUI packages; generated `GlobalUsings.g.cs` has `System.Windows.Forms`/`System.Drawing` but no `Microsoft.Maui.*`; no `bin/` output; `NotifyIcon` has no `Invoke` method (it is a `Component`, not a `Control`); `Application`/`Window` would be ambiguous between MAUI and WinForms implicit usings.
- **Verification:** Windows CI build — result recorded in 05.

### ISS-006 — IPC handshake deadlocks on Windows and drops the tray's first message (P0, Verified — see resolution log)
- **Files:** `Service/IPC/NamedPipeAuthenticator.cs:27-28`, `Service/IPC/NamedPipeServer.cs:52,65`; `TrayApp/Services/NamedPipeClient.cs:47,77-78,147`
- **Problem:** The authenticator reads the handshake with its own `StreamReader`, which buffers everything available on the pipe. The tray writes `NonceResponse` and immediately `StatusRequest`. If both arrive before the first read completes, `StatusRequest` sits in the discarded authenticator buffer and `HandleClientAsync`'s new reader never sees it. Additionally each `new StreamWriter(pipe, Encoding.UTF8)` emits a UTF-8 BOM, so simply reusing one reader yields `\uFEFF{...}` lines that fail JSON parsing.
- **Actual:** Intermittently the tray never receives its initial state (tooltip stuck at "Starting..."). **Expected:** every message after a successful handshake is processed.
- **Reproduction:** scratch experiment (anonymous pipe, two writers, reader-per-phase): second line lost; BOM-prefixed line → `JsonReaderException`.
- **Root cause:** Reader-per-phase on one stream + BOM-emitting writers.
- **Recommended fix:** One reader/writer pair per connection shared by handshake and message loop; BOM-less UTF-8 for writers; tolerate a leading BOM when parsing (backward compatibility with older trays).

### ISS-007 — Records dropped on 401/403 (P1)
- **File:** `Sync/ActivitySyncService.cs:163-170`
- **Actual:** On 401/403 the dequeued batch is discarded ("dropped pending re-enrollment"). An expired token or a transient auth outage permanently loses employee activity. **Expected:** keep records until a valid token is available (bounded by buffer capacity).
- **Fix:** requeue at front and back off.

### ISS-008 — Batch lost on unexpected exception; silent drops (P1)
- **File:** `Sync/ActivitySyncService.cs:81-131`
- **Problem:** Records are dequeued before work begins. Any exception not caught locally — e.g. `IOException`/`UnauthorizedAccessException` from `ReadDeviceJwt` (only `CryptographicException` is caught), `InvalidOperationException` from `JsonElement.Deserialize` on an undefined element — escapes to `ExecuteAsync`, which logs and continues: the batch is gone. Non-activity record types and `null` payloads are dropped without any log.
- **Fix:** guarantee requeue on every non-success path; log every dropped record with its eventId and reason.

### ISS-009 — No idempotency key (P1)
- **Files:** `Api/ActivityIngestModels.cs` (no event id), `Sync/ActivitySyncService.cs:142-151`
- **Problem:** If the backend commits a batch but the response is lost (timeout/reset), the client requeues and re-sends → duplicate snapshots → inflated activity. `CollectionRecord.EventId` exists but is not sent.
- **Fix:** send `event_id` per snapshot (or an `Idempotency-Key` header per batch) and have the backend de-duplicate. **Requires backend contract change → Escalated.**

### ISS-010 — Device JWT readable by any local user (P1) — see 02 §2
- **File:** `Security/CredentialStore.cs:9-18`, `Security/DeviceIdentityStore.cs:8-15`
- **Fix:** protected ACL (SYSTEM + Administrators) applied on every write, DPAPI entropy, atomic writes.

### ISS-011 — IPC has no real authentication (P1) — see 02 §3. **Escalated** (contract + Windows testing).

### ISS-012 — LL hooks without a message loop (P1)
- **File:** `Collectors/ActivityCountCollector.cs:111-136`; caller chain `CollectorCoordinator.ReconcileAsync` ← `NamedPipeClient.ReadLoopAsync` (thread-pool thread)
- **Problem:** Windows calls `WH_*_LL` hook procedures by sending a message to the installing thread, which must pump messages. Thread-pool threads do not. Result: callbacks never run (counts always 0) and every input event waits for `LowLevelHooksTimeout` before Windows skips/removes the hook → system-wide input lag.
- **Fix:** install/uninstall hooks on a dedicated thread running a message loop (e.g. the existing tray STA thread). Requires Windows device verification.

### ISS-013 — Pipe client never reconnects (P1)
- **File:** `TrayApp/Services/NamedPipeClient.cs:35-73`
- **Actual:** 5 attempts (~15–40 s) then gives up; after a successful session ends (service restart/upgrade) the method returns and never retries. Common at boot when the tray starts before the service. **Expected:** reconnect indefinitely with capped backoff.

### ISS-014 — Active/idle accounting errors (P1)
- **File:** `Collectors/ActivityCountCollector.cs:210-230`, `:96-104`
- **Problems:** (a) the final snapshot on stop (pause/break/disconnect) always reports a **full interval** even if only seconds elapsed — each pause/resume inflates active time by up to 60 s; (b) idle is derived only from time-since-last-input at the snapshot instant with a ≥ half-interval threshold — 50 s idle followed by 10 s input reports 60 s active; 29 s idle at the end reports 0 idle; (c) after sleep/hibernate the first snapshot still claims one interval.
- **Fix:** compute elapsed from a monotonic clock and accumulate idle time by sampling last-input time (pure, unit-testable calculator).

### ISS-015 — Start/stop race orphans the snapshot loop (P1)
- **Files:** `CollectorCoordinator.cs:36-58` (fire-and-forget `ReconcileAsync`), `ActivityCountCollector.cs:72-109`
- **Scenario:** Active → Paused → Active quickly: `StopAsync` sets `_running=false` then awaits the loop; concurrent `StartAsync` installs new hooks and a new CTS; resumed `StopAsync` disposes the *new* CTS without cancelling it and unhooks → orphan loop keeps emitting while the user is on break; next `StartAsync` is a no-op.
- **Fix:** serialize reconcile/start/stop behind one async lock and always reconcile to the latest state.

### ISS-016 — No single-instance guard (P1)
- **File:** `MauiProgram.cs`, `App.xaml.cs`. Two tray instances → two hook sets → doubled counts. **Fix:** per-session named mutex (`Local\ONEVO.Agent.TrayApp`).

### ISS-017 — Snapshot loop dies on first IPC write error (P1)
- **File:** `ActivityCountCollector.cs:194-208`. Only `OperationCanceledException` is caught; an `IOException` from a broken pipe faults the loop; `_running` stays `true` so the collector never restarts. **Fix:** catch/log per iteration.

### ISS-018 — Pipe server lifecycle faults (P2, Verified)
- **File:** `Service/IPC/NamedPipeServer.cs:33-46,48-56,118-125`; `AgentWorker.cs:47-52`
- **Problems:** (a) disposed by `AgentWorker.StopAsync` **and** by the DI container → second `CancelAsync` on a disposed CTS throws `ObjectDisposedException` during shutdown (verified experiment E4); (b) `CreateSecurePipe()` is outside the `try`, so one failure ends the accept loop silently and the service stops accepting trays while still "running"; (c) authentication timeouts/IO errors escape `HandleClientAsync` as unobserved task exceptions with no log.
- **Fix:** idempotent dispose; pipe creation inside the guarded loop with back-off; catch/log handshake failures.

### ISS-019 — ApiBaseUrl not validated (P2)
- **File:** `Configuration/OptionsValidation.cs`. `http://` or relative URLs are accepted. **Fix:** require absolute `https` URL when set.

### ISS-020 — Unbounded IPC line reads (P2)
- **Files:** `NamedPipeServer.cs:70-76`, `NamedPipeClient.cs:152-154`. **Fix:** bounded line reader (reject before buffering > 64 KB) and cap concurrent server instances.

### ISS-021 — Unbounded final flush (P2)
- **File:** `ActivitySyncService.cs:74-76` uses `CancellationToken.None`; with default HttpClient timeout (100 s) shutdown can exceed SCM's stop timeout and the service is killed. **Fix:** bound to a few seconds.

### ISS-022 — No user identity on records (P2) — see 02. **Escalated** (contract).

### ISS-023 — `DeviceId = Environment.MachineName` (P2)
- **File:** `ActivityCountCollector.cs:47`. Should come from enrolled `DeviceIdentity` (service should stamp it). Blocked on ISS-002.

### ISS-024 — Non-atomic secret writes (P2)
- **Files:** `CredentialStore.cs:18`, `DeviceIdentityStore.cs:15`. **Fix:** write temp + `File.Move(overwrite)`.

### ISS-025 — Observability gaps (P2)
- Tray logs only to the Debug provider (`MauiProgram.cs:21`) — nothing persisted in release. No file logging/rotation, no crash handler (`AppDomain.UnhandledException`), no correlation ID sent to the backend.

### ISS-026 — State not pushed; misleading disconnected state (P2)
- Service answers `StatusRequest` but never pushes changes; tray shows "Ready" (`Stopped`) when actually disconnected (`App.xaml.cs:29-33`).

### ISS-027 — Build outputs committed (P3, Verified)
- 363 of 427 tracked files are `bin/`/`obj/` (including machine paths `C:\HR\tray_app_maui\...`); HEAD commit deleted `.gitignore`.

### ISS-028 — No CI / installer / versioning (P3, Verified)
- No `.github/workflows`, no WiX/MSIX/Inno project, no service registration script, `ApplicationVersion` hard-coded to 1.

### ISS-029 — Dependency currency (P3, Verified)
- Microsoft.Extensions.* 10.0.0 → 10.0.14 available; ProtectedData 9.0.0 on a .NET 10 app. No known vulnerabilities reported.

### ISS-030 — Buffer cap and ordering (P3)
- `ActivityRecordBuffer.TryEnqueue` check-then-enqueue is not atomic; `QueueCapacityMb` is unused; `RequeueFront` appends to the back (reorders); caller ignores `false` (silent drop when full).

### ISS-031 — State machine nits (P3)
- `(_, Locked)` allows `Locked→Locked`; state is not persisted so a restart forgets enrollment/lock state.

### ISS-032 — Tray UI init (P3, Suspected)
- `TrayIconService.Initialize` blocks the UI thread up to 5 s; `UpdateState` before init is dropped; `App.CreateWindow` returns a `Window` with no page (blank window or exception for a tray-only app).

### ISS-033 — Response not disposed (P3)
- `ActivitySyncService.cs:142-176`.

### ISS-034 — Employee can exit tray (Policy)
- `TrayIconService.cs:76-80`. Needs a documented monitoring-policy decision. **Escalated.**

### ISS-035 — Pipe-client disposal throws on exit (P2, Verified) — found during Phase 2
- **File:** `TrayApp/Services/NamedPipeClient.cs` `DisposeAsync`; caller `App.xaml.cs` `window.Destroying += async (_, _) => …` (async void)
- **Problem:** disposing the `StreamWriter` flushes into a pipe the connection loop may already have closed → `ObjectDisposedException`; a second call throws on the disposed CTS. In an async-void handler either exception terminates the process on exit.
- **Evidence:** reproduced in 3 of 8 parallel test runs while stabilising the ISS-013 test; double-dispose reproduces deterministically.

---

## Resolution log (Phase 2)

All tests below ran on Linux (SDK 10.0.113) **and** on Windows CI with the pinned SDK 10.0.300 (run 203, commit `8d808a7`: 89/89 passed). "Red on old code" = the new test was run against the previous implementation (with only the test seam applied) and failed.

| Issue | Commit | Tests added | Red on old code | Verification |
|---|---|---|---|---|
| ISS-006 | `3fdddad`, `8d808a7` | `IpcLineCodecTests` (10), `NamedPipeAuthenticatorTests` (5), `NamedPipeClientTests` (2), `NamedPipeServerTests.ProductionPipe_WithAcl_…` | Yes (client tests 2/2) | Windows CI: production ACL pipe completes handshake + status round trip. `Characterization_OriginalHandshakeWriters_DeadlockOnWindows` passed on Windows, proving the **original** handshake (both ends create `StreamWriter(Encoding.UTF8){AutoFlush=true}` before reading → synchronous BOM write on a zero-buffer pipe) deadlocks on every connection. Priority raised to P0. |
| ISS-007, 008, 021, 030, 033 | `14507cf` | `ActivitySyncServiceTests` (13), `ActivityRecordBufferTests` (3) | Yes (7 tests) | Passed Linux + Windows. Note: the concurrency-cap test also passes on old code (race rarely hit), so ISS-030's cap race is hardening, not reproduced. `CancelledDuringSend` fails on old code because cancellation was swallowed, not because data was lost. |
| ISS-018 | `d522795` | `NamedPipeServerTests` (3 + 1 Windows-only) | Yes (3/3) | Passed Linux (15/15 repeats) + Windows. `ClientDroppingDuringHandshake` reproduces via Linux pipe semantics; on Windows the old behaviour would delay rather than reset the next client. |
| ISS-010, 024 | `d7547e8` | `SecureStorageTests` (1 + 3 Windows-only) | n/a (old code had no ACL handling; the assertions cannot hold without it) | Windows CI: folder DACL protected, only SYSTEM + Administrators, owner Administrators, squatted folder reclaimed, DPAPI round trip. |
| ISS-019 | `694d496` | `OptionsValidationTests` (8) | — | Passed. HTTP timeout still unapplied (no HttpClient registration — ISS-001). |
| ISS-013 | `dbe7755` | `NamedPipeClientReconnectTests` (1) | Yes | Passed Linux + Windows. |
| ISS-015 | `4eb7058` | `CollectorCoordinatorTests` (1) | Yes — old code ran **6 concurrent** Start/Stop calls (3/3 runs) | Passed Linux + Windows. |
| ISS-014, 017 | `970f55c` | `ActivityIntervalTrackerTests` (8), `ActivityCountCollectorTests` (3) | Not runnable (calculation was not separable); old values derived from the old formula are recorded in test comments | Unit-verified. **Real-device verification pending** (hooks + GetLastInputInfo cannot run in CI). |
| ISS-035 | `b60d645` | `NamedPipeClientDisposeTests` (2) | Yes (double dispose, deterministic) | Passed Linux (8/8 full-suite repeats) + Windows. |
| ISS-027, 028 | `23118b7`, `ae878c8`, `d83a964` | CI workflow | — | CI runs on every push; tray job intentionally red until ISS-005 is resolved. |
| ISS-029 | `ef81a0c` | — | — | `dotnet list package --vulnerable`: none; all tests pass. |

### ISS-005 update (Verified)
Windows CI (runs 198, 199, 201, 203): `dotnet build ONEVO.Agent.TrayApp.csproj` reports "Build succeeded, 0 errors" in ~0.4 s and produces **no assembly**; the project has no `Platforms/Windows` head (WinUI `App.xaml`, manifest) that a MAUI Windows app requires. The original CI-less repository therefore never had a runnable tray. Making it buildable requires either adding MAUI Windows platform scaffolding and resolving the MAUI/WinForms namespace conflicts, or re-platforming the tray (a tray-only UI does not need MAUI). This is an architecture decision → **Escalated**. Until then every tray-side fix is verified only through `tests/ONEVO.Agent.TrayApp.Tests`, which compiles the tray's UI-free sources.

### Open P0/P1 items after Phase 2
ISS-001, 002, 003, 004, 005, 009, 011, 022 (escalated — need product/backend/architecture decisions); ISS-012, 016 (need a buildable tray and a Windows device).
