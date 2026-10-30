# 03 — Issue Register — `develop`

Final gap audit of `9a13436` (2026-10-30), with the AUD-001…036 status matrix: **[14](14-final-uat-gap-audit.md)**.

Classification: **Verified** (reproduced by test/experiment), **Confirmed (code)** (follows from code, not executed), **Observation** (design limitation / decision needed).

## Findings

| ID | P | Component | Title | Class | Status |
|---|---|---|---|---|---|
| UAT-01 | P1 | Tray `ActivityCountCollector` | Activity snapshots over-report active time | Confirmed (code); fix unit-tested | **Fixed in PR #100** (`61e43f9`, `f987d83`) — device check pending |
| UAT-02 | P3 | Tray `SessionDayMetrics` | Idle accumulated twice (two collectors), value unused | Confirmed (code) | **Fixed** (`b347107`, step 2 PR, CI pending) |
| UAT-03 | P3 | Tray logging / privacy | Per-snapshot keyboard/mouse counts written to `tray.log` | Confirmed (code) | Open — policy decision |
| UAT-04 | — | Service `PresenceSession` | Idle end detected at 60 s sample resolution | Observation | Accepted limitation (not changed) |
| UAT-05 | P2 | Release / git | GitHub default branch `main` is still `5cca93e` | Verified | Open — release process (AGT-03) |
| UAT-06 | P3 | Tests | ~135 Service tests fail off-Windows without being marked Windows-only | Verified | Open — informational |
| UAT-07 | **P1** | Packaging | Released MSIX does not contain or install the Agent Service | Verified (manifest, release.yml) | **Escalated** — installer decision |
| UAT-08 | **P1** | Packaging / lifecycle | Tray does not start at logon (no startup task / Run key) | Verified (manifest, code search) | **Escalated** — installer decision |
| UAT-09 | P3 | Security | Credential confidentiality relies on the folder ACL (DPAPI LocalMachine) | Confirmed (code) | Device verification (11 §F) |
| UAT-10 | Info | Release | Production code-signing certificate needed; dev cert requires manual trust | Verified (`release.yml`, `build-msix.ps1`) | T17 |
| UAT-11 | **P1** (was P3) | Configuration | No UAT configuration; shipped `ApiBaseUrl` is the dev host (AUD-013/030) | Verified (`appsettings.json`) | **Escalated** — UAT URLs needed (14 §G D-04) |
| UAT-12 | **P1** | Service buffer / privacy | Face photos kept unencrypted (base64 JSON) in SQLite and retained after upload | Verified (code + measurement) | **Escalated** — privacy/retention decision |
| UAT-13 | P2 | Service buffer / storage | Delivered rows retained until the 100 MB limit (≈2–4 months); then a blocking full `VACUUM` | Verified (measurement) | **Fixed in PR #100** (`a8dab05`); CI green on `86aa632` (Service 518/518) |
| UAT-14 | P3 | Packaging / size | MSIX carried 45 images no screen uses | Verified (measurement build) | **Fixed in PR #101**: MSIX 48.25 → 43.85 MB |
| UAT-15 | P2 | Backend contract (AUD-007) | `tray/daily-tasks` and attendance day fields not on backend `development` | Confirmed (code, both repos) | Escalated — backend branch decision |
| UAT-16 | P2 | Backend ingest (AUD-007) | No replay de-dupe for app-usage, device-state, meeting signals | Confirmed (code) | Escalated — backend contract |
| UAT-17 | P1 | Service IPC (AUD-008) | Serial IPC dispatch; 17 non-lifecycle handlers call the backend without a budget | Confirmed (code); fix verified by CI run 221 (Service 544/544) | **Partly fixed**: part 1 in PR #102 (`2a41be6`); part 2, read-only budgets, in PR #103 (`923692a`, CI run 224: Service 559/559): independent requests off the read loop. Per-handler time budgets still open |
| UAT-18 | **P0** | Service/Tray privacy (AUD-010) | Consent, policy and time-off gates hard-coded true; capture starts before server policy | Confirmed (code, both repos) | **Escalated** — product/privacy rules |
| UAT-19 | P3 | Packaging (AUD-029) | Windows version declarations inconsistent | Confirmed (code) | Open |
| UAT-20 | P3 | Tests (AUD-017/034/036) | No whole-suite guard for real data paths | Confirmed (code) | Open |
| UAT-22 | P3 | Build | 49 XamlC `XC0025` warnings in the TrayApp build | Verified (CI 220 log) | **Fixed** (`40efd1a`, step 2 PR, CI pending) |
| UAT-21 | P2 | Release (R10) | No tag, release or release branch; footprint check not in CI | Verified (GitHub) | Open — release process |

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
- Confirmed on a real build (2026-10-30, measurement run 40885438847): the unpacked MSIX contains no `ONEVO.Agent.Service*.exe`.
- **Needs a packaging decision:** MSIX `desktop6:Service`, or an MSI/bootstrapper.

### UAT-08 — Tray does not start at logon (P1)
- There is no `windows.startupTask` in the manifest, and no Run key or scheduled task anywhere in the code or scripts.
- After a reboot during a workday, the Service receives no tray IPC. `PresenceSession`'s gap fallback then counts the time as idle until the employee opens the tray, and no activity is captured in the meantime.
- **Fix with UAT-07's installer work:** a startup task, or register the tray at install.

### UAT-11 — Environment endpoint (P1, raised from P3 by the final gap audit: AUD-013/030 are P0 release blockers)
- The shipped `appsettings.json` targets `https://onexso.com:7229` for every environment unless overridden. Confirm the UAT and production values with the backend team.
- `PortalDevelopmentOrigins` / `PortalDevelopmentPorts` are honoured only when the host environment is Development (`PortalOriginValidator`), so they are not a production exposure.

### UAT-12 — Face photos kept unencrypted and after upload (P1, privacy)
- **Write path:** `PhotoCaptureWindowViewModel.SubmitFacePhotoRecordAsync` puts the JPEG into `FacePhotoPayload.Data` as **base64**. The record is stored in `collection_records.payload_json` like any other row: not DPAPI-protected, unlike screenshots in the evidence spool.
- **Retention:** after upload, `MarkAcknowledged` only sets `status = 'synced'`. The row, with the photo and the capture GPS, stays on disk until the database reaches 100 MB. That is ≈58–110 days at one photo a day (measured, see 13).
- **Size:** the camera picks its largest format ≥ 640×480. Base64 adds 33%, so a 400 KB JPEG costs ≈ 560 KB of database per photo.
- **Recommendation:** treat face photos like other evidence.
  - Spool the bytes DPAPI-encrypted (`EvidenceSpoolStore`), keep only a reference in the row, and delete after delivery.
  - Or, at minimum, delete the row's photo data on acknowledgement.
- Biometric retention is a policy decision → **Escalated**.

### UAT-13 — Delivered rows retained; blocking VACUUM (P2)
- Synced rows of every type stay until the 100 MB byte limit. Then `HasRoomUnlocked` deletes them and runs a full `VACUUM` while holding the buffer lock. A full `VACUUM` rewrites the whole file and needs up to the same amount of free disk again.
- **Measured** with the real `ActivityRecordBuffer` and a synthetic 8-hour day: 1,681 rows/day, 0.91–1.73 MB/day after acknowledgement. Deleting the synced rows plus `VACUUM` brings the file back to 32 KB.
- **Fix (`a8dab05`):**
  - `PurgeDelivered` deletes synced rows queued more than 24 h ago, after every flush and once at open.
  - Inside the 24 h window, `INSERT OR IGNORE` still de-duplicates late re-submits. After it, a replay is refused (snapshots > 24 h), upserted (work sessions) or de-duplicated by idempotency key (face photos).
  - Pending, quarantined and synced inactivity-attempt rows are never touched.
  - The file runs with `auto_vacuum = INCREMENTAL` (existing files are converted once at open), and freed pages are returned with `incremental_vacuum`.
  - Housekeeping failures are logged and never stop capture, upload or startup.
- **Measured** (1,681 rows + a 200 KB photo per day, uploaded daily):
  - `develop`: 26.9 MB after 30 days, reaching 100 MB around day 83.
  - With the fix: ~1.2 MB steady state (one day of delivered rows).
  - 30 purges took 339 ms in total.
- **Tests:** `ActivityRecordBufferRetentionTests` (4):
  - only the right rows are deleted;
  - re-submit dedupe inside the window;
  - the file shrinks > 10×;
  - a legacy file is converted with its pending rows kept.

### UAT-02 — Idle accumulated twice in tray day metrics (P3)
- `ActivityCountCollector` and `DeviceStateCollector` both call `ISessionDayMetrics.AddIdleSample` for the same idle time.
- `SessionDayMetrics.TotalIdle` has no production reader: the session idle shown to users comes from the Service (`SessionSnapshot.AccumulatedIdle`). There is therefore no visible impact today, but anyone who starts using `TotalIdle` gets roughly double the idle.
- **Recommendation:** keep a single source (DeviceState, which uses the policy idle threshold) or remove `TotalIdle`.
- **Fix (`b347107`):** `ActivityCountCollector` no longer takes `ISessionDayMetrics`, so it cannot add idle; `DeviceStateCollector` is the only source. The constructor change is the guard (a behaviour test cannot observe a dependency that no longer exists). Existing collector tests updated.

### UAT-03 — Activity counts in `tray.log` (P3, privacy)
- `ActivityCountCollector` logs `snapshot #N kb=… mouse=…` at Information. `TrayLogging` persists Information and above to the rolling `tray.log` (5 MB × retained files).
- These are counts only — no keys, coordinates or titles — but they form a persisted per-minute activity trail on the device, outside the backend's access controls.
- **Recommendation:** log at Debug, or drop the counts. Needs a monitoring-policy owner decision (assignment §7).

### UAT-04 — Idle end at sample resolution (observation)
- `PresenceSession.ApplyDeviceStateIdle` ends an idle pause at the DeviceState sample time (every 60 s), so each idle episode can include up to ~60 s that was actually worked.
- Using `CapturedAt − IdleSeconds` gains only seconds, because the user's last input is usually just before the sample. It would also change backend-visible attendance numbers and an existing test's expectation, so it was not changed.
- Shortening the DeviceState sample window is the effective lever if more precision is required.

### UAT-05 — Stale default branch (P2)
- `main` (the GitHub default branch) is `5cca93e`, 340 commits behind `develop` at `9a13436`. Fresh clones and tools that use the default branch see a scaffold without most features.
- Already identified as AGT-03. The planned `develop → main` release PR has not happened yet.

### UAT-06 — Platform-specific tests not marked (P3)
- On Linux, 135 of 514 Service tests fail: `PlatformNotSupportedException` for DPAPI, ACL and SystemEvents, plus Windows path semantics.
- CI is Windows-only, so this is informational. Marking them Windows-only would let the platform-neutral majority run anywhere.

### UAT-15 — Daily-tasks endpoint not on backend `development` (P2, AUD-007)
- **Files:** tray `ONEVO.Agent.Service/Api/AgentApiRoutes.cs` (`TrayDailyTasks`), `OnevoApiClient.GetDailyTasksAsync`, `TrayAttendanceStatusPayload`; backend `TrayAttendanceController.cs`, `TrayAttendanceStatusDto.cs`.
- **Actual:** backend commit `51d84d4c` (adds `GET …/tray/daily-tasks` and `day_clocked_in_at_utc` / `day_clocked_out_at_utc`) is only on `origin/fix/dapi-seeder-skip-today`, not on `origin/development` (`8ce94e8`). The other 37 tray routes match a backend route and verb.
- **Effect:** the call returns 404; the tray logs a warning and the daily summary shows no tasks. Attendance fields default to null, so a tray restarted after clock-out cannot show the finished day. No crash, no data loss.
- **Recommendation:** merge the commit (with its tests) into `development`, or remove the feature from UAT scope. Add a route contract test (14 §F.1 AI-2, AI-4).
- **Manual:** 11 §H.

### UAT-16 — No replay de-dupe for three ingest families (P2, AUD-007)
- **Files:** backend `IngestAppUsageSnapshotsCommandHandler.cs`, `IngestDeviceStateSnapshotsCommandHandler.cs`, `IngestMeetingSignalsCommandHandler.cs`; index configs `AppUsageSnapshotConfiguration.cs`, `DeviceStateSnapshotConfiguration.cs`, `MeetingSignalConfiguration.cs` (indexes are not unique).
- **Actual:** only activity snapshots skip already-ingested `captured_at` values. A 202 lost after commit, followed by the tray's retry (AGT-01, AUD-023), inserts the rows again.
- **Expected:** idempotent replay, as planned in `docs/fixes/2026-10-12-agt-sec-remediation-plan.md` (AGT-01 step 3, not implemented).
- **Recommendation:** natural-key de-dupe `(tenant, device, captured_at)` + unique index migration (created, applied by a developer). Contract change → Escalated.
- **Manual:** 11 §D (drop the network after send).

### UAT-17 — Serial IPC dispatch with unbudgeted backend calls (P1, AUD-008 remainder)
- **Files:** `ONEVO.Agent.Service/IPC/NamedPipeServer.cs` (read loop `await MessageReceived(...)`), `AgentWorker.HandleMessageAsync`.
- **Actual:** each connection's messages are processed one at a time. These handlers call the backend with `CancellationToken.None`, bounded only by `HttpTimeoutSeconds` (30 s) per attempt and the retry policy: activation code, device-pairing start, location change submit/pending/respond, daily tasks, generic-notification ack, work-location confirm, face photo validate, face reference status, permission consent, face enrol commit, legal acceptance, update check, logout (15), plus biometric enrolment start/finish, which call the backend through `EnrollmentCoordinator` with `CancellationToken.None` (17 in total). The assignment counts 13; the difference is not resolved because the original list is in the missing register (14 §A).
- **Effect:** a clock-in sent while one of these is waiting is not read until it finishes; the tray's lifecycle reply timeout can expire although the Service is healthy.
- **Recommendation:** per-message time budgets and bounded concurrent dispatch, keeping lifecycle commands ordered; tests with a hanging stub backend (AI-1).
- **Fix, part 1 (`2a41be6`, branch `claude/upbeat-hamilton-ebza6u`, not yet in a PR):**
  - `IPC/IpcDispatch.cs` lists 10 message types whose handlers only read the device credential, call the backend and reply by correlation id: daily tasks, location change submit/pending/respond, work-location confirm, face reference status, permission consent status/submit, generic-notification ack, update check. `NamedPipeServer` runs these off the read loop, at most 4 per connection; when all 4 are busy the loop waits for a slot.
  - Kept on the read loop, in arrival order: everything else. That includes the 7 backend-calling handlers that also change Service state: activation code, device pairing, logout, biometric enrolment start/finish, face photo validate (stages the photo), face enrol commit, legal acceptance (clears the pending challenge). Unknown types stay on the loop too.
  - A failing independent handler is logged at Warning and no longer drops the connection.
  - The tray already matches replies by correlation id (`NamedPipeClient._pending`), and server writes are serialized per connection (`IpcConnection`), so out-of-order replies are safe.
- **Tests:** `NamedPipeServerDispatchTests` (4 pipe tests + 2 classification theories, 22 cases):
  - a pending daily-tasks call does not hold back a later lifecycle command; on the old code this send or read times out;
  - ordered messages keep order with no overlap;
  - no more than `MaxConcurrentRequestsPerConnection` run at once;
  - a throwing independent handler leaves the connection up; on the old code the connection dropped.
  - The pipe harness moved unchanged from `NamedPipeServerBroadcastTests` into `TestPipeConnection`.
- **Verification:** PR #102 CI run 221 (<https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41004208690>): success, Service 544/544 (+26 new), 0 build warnings in Service. Not run locally (SDK download blocked).
- **Remaining (part 2):** per-handler time budgets. One backend call can still hold a slot for about 2.5–3 minutes: 5 attempts × `HttpTimeoutSeconds` 30 s, plus back-off of up to ≈15 s. Four such calls at once would block the loop again. The 7 stateful handlers above still run serially without a budget.
- **Fix, part 2 (`923692a`):** `IpcDispatch.ReadOnlyBudget` gives the 6 read-only requests the same budget the tray waits for their reply: daily tasks 10 s, pending location check 10 s, face reference 15 s, consent status 8 s, update check 20 s, delivery ack 30 s. On expiry the handler sends its normal failure reply. Tests in `AgentWorkerTests`: a backend that never answers → failure within the budget (before: never returned); budget table theories.
- **Still open (needs decision D-15):** writing requests (consent submit, location change submit/respond, work-location confirm, activation, pairing, logout, biometric, face validate/commit, legal acceptance) still have no budget. Cancelling them can leave the backend committed while the Service drops the result, so each needs a recovery path first, as AUD-008 did for clock actions.
- **Manual:** 11 §C (clock in while the backend is slow, e.g. by throttling with a proxy).

### UAT-18 — Monitoring privacy gates not enforced (P0, AUD-010)
- **Files:** `ONEVO.Agent.Service/AgentWorker.cs` `ApplyEnrollmentGates` (sets `ConsentValid`, `PolicyAllowsCollection`, `NotOnApprovedTimeOff` to `true`, with a comment that the real sources are "not yet built"); `ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs` `CreateLocalDefaultPolicy` (`ActivitySignalEnabled = true` when Active and no policy received); backend ingest handlers check the feature toggle (`_toggleResolver`) but not consent or leave; `TrayClockInCommandHandler` has no leave check.
- **Actual:** after enrolment, clock-in and keyboard/mouse activity capture proceed whether or not the employee's consent is recorded and whether or not they are on approved leave. App usage and screenshots do wait for a real policy.
- **Expected:** rules approved by product and privacy owners, enforced before any collector starts.
- **Recommendation:** decide the rules (14 §G D-05), then wire consent (`permission-consent`), policy (`PolicyCache`) and leave (backend field) into `LifecycleGate`, drop capture on the local default policy, test each gate (AI-5).
- **Manual:** 11 §I.

### UAT-19 — Windows version declarations (P3, AUD-029)
- `Package.appxmanifest` `MinVersion 10.0.17763.0`, `MaxVersionTested 10.0.19041.0`; project `SupportedOSPlatformVersion`/`TargetPlatformMinVersion 10.0.19041.0`; `release.yml` registers `minimumWindowsVersion 10.0.19041.0`. Align with the supported matrix (Windows 10 22H2, Windows 11) in the installer work.

### UAT-20 — No whole-suite real-path guard (P3, AUD-017/036)
- Isolation is per path: `TestAgentDataRoot` (`AgentDataPaths.OverrideRootForTests`, AUD-034) and `TestAssemblySetup` (`TrayBootLog.FallbackPath`, AUD-036). The only guard test, `TestPathIsolationTests`, checks the tray-boot fallback path. A new path added later is not covered automatically.
- **Recommendation:** a CI step that hashes/lists `%ProgramData%\ONEVO` and `%LocalAppData%\ONEVO` before and after the test run and fails on any change.

### UAT-21 — No release candidate process in use (P2, R10)
- No tags, no GitHub release, no `release/*` branch; `main` stale (UAT-05). `scripts/check-footprint.ps1` is manual only. R10 needs: release PR, tag, signed MSIX from `release.yml`, version matrix, release notes.

### UAT-22 — XamlC XC0025 warnings (P3)
- **Evidence:** CI run 220 (`9a13436`), step "Build TrayApp": `49 Warning(s)`, all `XC0025` ("Binding was not compiled because it has an explicitly set Source property…"), e.g. `…HeaderBar.xaml`, `…StatusBar.xaml`. Shared and Service: `0 Warning(s)`.
- **Effect:** those bindings use reflection at runtime; no functional defect.
- **Recommendation:** enable `MauiEnableXamlCBindingWithSourceCompilation` (or add `x:DataType` on the bindings) and keep the build warning-free.
- **Fix (`40efd1a`, corrected in the next commit):** `MauiEnableXamlCBindingWithSourceCompilation=true`, plus `x:DataType` set to the control's own class on the root of the 9 controls whose bindings all use `Source={x:Reference Root}` (47 bindings). Paths were checked against the controls' public properties before pushing.
  - First attempt (CI run 226, green but 52 warnings): without `x:DataType` the 47 bindings gave `XC0022`, and the `x:DataType="{x:Null}"` opt-out on the 2 page bindings gave `XC0023`. Both were removed.
  - The 2 page bindings through `BindingContext` (LegalConsentPage "View", WorkLocationPage option tap) are unchanged from before. Their warning count is in the next CI run.
- **Manual:** 11 §E (both page actions and the 9 controls).

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
