# 04 — Test Coverage Matrix (T08)

133 test classes in three projects. The Windows CI coverage gate enforces per-assembly floors (`coverage-thresholds.json`, AUD-025).

| Project | Test classes by folder |
|---|---|
| `ONEVO.Agent.Shared.Tests` | 9 (IPC envelopes/messages, JSON file logger) |
| `ONEVO.Agent.Service.Tests` | Sync 10, Lifecycle 6, Security 6, Api 5, Buffer 4, IPC 4, Biometrics 2, Configuration 2, Diagnostics 1, Enrollment 1, **EndToEnd 1**, root 20 |
| `ONEVO.Agent.TrayApp.Tests` | Services 24, ViewModels 16, Collectors 9, Capture 4, Notifications 3, Controls 2, Security 2, Views 1, root 1 |

## Matrix

✅ automated · ◐ partial · 🖐 manual only

| Behaviour | Unit | Integration / in-process | E2E | Manual | Notes |
|---|---|---|---|---|---|
| IPC handshake, framing, client verification | ✅ | ✅ | — | 🖐 | `IPC/*` (Service) |
| IPC dispatch: independent requests off the read loop, ordering, bound (AUD-008, UAT-17) | ✅ | ✅ (real pipe pair) | — | 🖐 11 §C | `NamedPipeServerDispatchTests` (branch `claude/upbeat-hamilton-ebza6u`, CI pending) |
| Enrollment / activation / token refresh | ✅ | ✅ (stub HTTP) | — | 🖐 | `DeviceTokenRefreshTests`, `Enrollment` |
| Clock-in/out, breaks, recovery (expired token, already done, break race) | ✅ | ✅ | ✅ T15 | 🖐 | `Lifecycle/*`, `WorkdayOfflineRecoveryFlowTests` |
| Attendance accounting (`PresenceSession`: idle, gap, sleep, resume) | ✅ | — | ◐ (T15) | 🖐 | `PresenceSessionTests`, `PresenceSessionResumeTests`, `DeviceStateIdleHandlerTests` |
| Activity accounting (snapshot active/idle) | ✅ (this PR) | — | — | 🖐 | `ActivityIntervalTrackerTests`, `ActivityCountCollectorAccountingTests` |
| LL input hooks | — | ✅ (interactive desktop) | — | 🖐 | `ActivityCountCollectorTests` (`Category=InteractiveDesktop`) |
| Durable queue (capacity, reserve, corrupt DB, quarantine, owner) | ✅ | ✅ (SQLite) | ◐ | — | `Buffer/*` |
| Sync outcomes (2xx/4xx/401/5xx, isolation, backoff, stale) | ✅ | ✅ (stub HTTP) | ✅ T15 outage | 🖐 UAT backend | `Sync/*` |
| Policy gating of collectors | ✅ | — | — | 🖐 | Collector/coordinator tests |
| Screen lock / sleep handling | ✅ | — | — | 🖐 | AUD-014 tests |
| Single instance, offline UI | ✅ | — | — | 🖐 | AUD-005/006 |
| Face check, camera, biometrics | ✅ (VMs, services) | — | — | 🖐 | Camera hardware needed |
| Logging, correlation ids, crash capture | ✅ | — | — | 🖐 | `Diagnostics`, `TrayBootLogTests` |
| Installer / upgrade / autostart | — | — | — | 🖐 | No automation; UAT-07/08 |
| Live backend contract | — | — | — | 🖐 | Postman collection `docs/postman/` |
| Resource footprint | — | `scripts/check-footprint.ps1` (manual run) | — | 🖐 | Idle budgets only (13) |

## Coverage (develop baseline, CI run 197)

| Assembly | Line (floor) | Branch (floor) |
|---|---|---|
| Shared | 46.8% (26%) | 87.5% (0%) |
| Service | 75.1% (72%) | 68.1% (63%) |
| TrayApp | 47% (45%) | 26.3% (24%) |

For this PR's coverage see [05](05-test-execution-report.md).
