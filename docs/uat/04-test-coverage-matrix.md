# 04 — Test Coverage Matrix (T08)

Test projects:

| Project | Target | What it tests | How tray code is reached |
|---|---|---|---|
| `tests/ONEVO.Agent.Shared.Tests` | net10.0 | IPC envelope + line codec | — |
| `tests/ONEVO.Agent.Service.Tests` | net10.0-windows | State machine, buffer, sync, pipe server/authenticator, options, secure storage | — |
| `tests/ONEVO.Agent.TrayApp.Tests` (new) | net10.0-windows | Pipe client, collector coordinator, activity accounting | Compiles the tray's UI-free sources (`Services/NamedPipeClient.cs`, `Collectors/*`, `Interop/*`, `Security/*`) because the MAUI app cannot be referenced (and does not build — ISS-005) |

Windows-only tests use `[WindowsFact]` and are skipped on Linux; they run in Windows CI.

## Matrix

Legend: ✅ automated and passing · ◐ partial · ❌ none · ⛔ not possible (feature absent) · 🖐 manual only

| Component / behaviour | Unit | Integration (real pipes / HTTP stub) | Contract | E2E | Manual (device) | Tests |
|---|---|---|---|---|---|---|
| IPC envelope & framing | ✅ | — | ✅ | — | — | `IpcEnvelopeTests`, `IpcLineCodecTests` |
| IPC handshake (service side) | ✅ | ✅ | ✅ | ❌ | 🖐 | `NamedPipeAuthenticatorTests`, `NamedPipeServerTests` (incl. production ACL pipe on Windows) |
| IPC client (tray side): handshake, reconnect, dispose | — | ✅ | ✅ | ❌ | 🖐 | `NamedPipeClientTests`, `NamedPipeClientReconnectTests`, `NamedPipeClientDisposeTests` |
| Pipe server lifecycle (accept loop, dispose) | — | ✅ | — | — | — | `NamedPipeServerTests` |
| IPC client authorization (who may connect) | ❌ | ❌ | — | — | 🖐 | ISS-011 escalated |
| State machine transitions | ✅ | — | — | — | — | `AgentStateMachineTests` (19) |
| State transitions driven by enrollment / policy | ⛔ | ⛔ | ⛔ | ⛔ | ⛔ | not implemented (ISS-001) |
| Activity buffer (FIFO, requeue order, cap) | ✅ | — | — | — | — | `ActivityRecordBufferTests` |
| Durable offline queue / restart recovery | ⛔ | ⛔ | — | ⛔ | ⛔ | RAM-only (ISS-004) |
| Sync: success / 401 / 403 / 5xx / network / exception / cancel / corrupt records / ordering / bounded stop | ✅ | ✅ (stub `HttpMessageHandler`) | ✅ (wire field names, route, Bearer, no `tenant_id`) | ❌ | — | `ActivitySyncServiceTests` (13) |
| Idempotency / duplicate suppression | ❌ | ❌ | ❌ | ❌ | — | needs backend contract (ISS-009) |
| Live backend contract | — | — | ❌ | ❌ | 🖐 | no backend available |
| Options validation (https, ranges) | ✅ | — | — | — | — | `OptionsValidationTests` (8) |
| Credential / identity storage (ACL, owner, atomic write, DPAPI) | ✅ | ✅ (Windows) | — | — | 🖐 (on a domain-joined device) | `SecureStorageTests` |
| Active/idle seconds calculation | ✅ | — | — | — | 🖐 | `ActivityIntervalTrackerTests` (8) |
| Snapshot emission (partial interval, failure resilience) | ✅ | — | — | — | 🖐 | `ActivityCountCollectorTests` (3) |
| Collector start/stop orchestration | — | ✅ | — | — | 🖐 | `CollectorCoordinatorTests` |
| Low-level keyboard/mouse hooks | ❌ | ❌ | — | — | 🖐 | requires interactive desktop (ISS-012) |
| Foreground process name scrubbing | ❌ | — | — | — | 🖐 | `PrivacyScrubber` uses user32 |
| Tray icon / UI / single instance | ❌ | ❌ | — | ❌ | 🖐 | tray does not build (ISS-005, ISS-016) |
| Login / enrollment / token refresh | ⛔ | ⛔ | ⛔ | ⛔ | ⛔ | ISS-002 |
| Clock-in/out, breaks | ⛔ | ⛔ | ⛔ | ⛔ | ⛔ | ISS-003 |
| Sleep / hibernate / lock / logoff | ◐ (calculation only) | ❌ | — | ❌ | 🖐 | `ResumeFromSleep_…`; no power/session event handling exists |
| Installer / upgrade | ⛔ | ⛔ | — | ⛔ | ⛔ | no installer (ISS-028) |

## Line coverage (measured)

Measured with coverlet on Linux (Windows-only tests skipped there, so storage/ACL code is under-reported; the tray's linked sources compile into the test assembly, which coverlet excludes by default). Windows CI uploads Cobertura reports per run in the `test-results` artifact.

| File | Lines covered |
|---|---|
| `Service/Sync/ActivitySyncService.cs` | 47/51 (92%) |
| `Service/Buffer/ActivityRecordBuffer.cs` | 30/30 (100%) |
| `Service/AgentStateMachine.cs` | 23/23 (100%) |
| `Service/IPC/NamedPipeAuthenticator.cs` | 21/23 (91%) |
| `Service/IPC/NamedPipeServer.cs` | 28/41 (68%) — ACL pipe creation covered only on Windows |
| `Service/Configuration/OptionsValidation.cs` | 14/18 (77%) |
| `Service/Security/SecureAgentStorage.cs` | 19/35 (54%) — ACL branch covered only on Windows |
| `Service/Security/CredentialStore.cs` | 0/28 on Linux — covered by Windows-only tests |
| `Shared/IPC/IpcLineCodec.cs` | 18/19 (94%) |
| `Service/AgentWorker.cs`, `Program.cs`, `Policy/PolicyCache.cs` | 0% — host wiring; no behaviour beyond ISS-001 scope to test |
