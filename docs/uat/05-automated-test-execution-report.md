# 05 — Automated Test Execution Report (T08)

## 1. Pipeline

`.github/workflows/ci.yml`, triggered on every push and pull request.

| Job | Runner | Steps | Gate |
|---|---|---|---|
| `core` | windows-latest, SDK from `global.json` (10.0.300) | restore → vulnerable-package check → Release build (warnings as errors) → tests (TRX + Cobertura, `--blame-hang-timeout 3m`) → `dotnet publish` service (win-x64, framework-dependent) | Any failing command fails the job (`bash -eo pipefail`); 30 min job timeout |
| `tray` | windows-latest + `maui-windows` workload | build TrayApp → **verify a TrayApp binary exists** | Intentionally red until ISS-005 is resolved |

Artifacts per run: `test-results` (TRX + coverage), `onevo-agent-service-1.0.0-ci.<run>` (published service), `tray-build-log` (binlog).
Versioning: CI builds are stamped `1.0.0-ci.<run_number>` (`-p:Version`). There is no release/tag pipeline yet.

## 2. Results of record

**Windows CI run 203** — commit `8d808a7` — <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/40859576861>

| Test project | Total | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|---|
| ONEVO.Agent.Shared.Tests | 17 | 17 | 0 | 0 | ~2 s |
| ONEVO.Agent.Service.Tests | 55 | 55 | 0 | 0 | 13.9 s |
| ONEVO.Agent.TrayApp.Tests | 17 | 17 | 0 | 0 | 5.7 s |
| **Total** | **89** | **89** | **0** | **0** | |

Windows-only tests executed in that run: `ProductionPipe_WithAcl_CompletesHandshakeAndStatusRoundTrip`, `Credential_RoundTrips`, `CredentialFolderAndFile_AreNotAccessibleToStandardUsers`, `PreCreatedFolderGrantingUsers_IsReclaimed`, `Characterization_OriginalHandshakeWriters_DeadlockOnWindows`.

Service artifact `onevo-agent-service-1.0.0-ci.203` was published. The `tray` job failed as designed: no TrayApp binary (ISS-005).

**Local (Linux, SDK 10.0.113, `global.json` relaxed in a scratch copy only)** — final branch head: Shared 17/17, Service 50 passed + 5 Windows-only skipped, TrayApp 17/17.

The final commits after run 203 (`ef81a0c` dependency servicing update and documentation) are covered by the CI run for the branch head; check the latest run on the branch before sign-off.

## 3. Run history and how failures were resolved

| Run | Commit | Result | Notes |
|---|---|---|---|
| 198 | `23118b7` | core ✅ (26 baseline tests), tray ✅ | **False green**: the tray build compiled nothing in 0.36 s. Led to the binary check (ISS-005). |
| 199, 200 | `ae878c8`, `d7547e8` | cancelled | Test step hung on Windows: a test awaited a pipe write before the peer read. Windows pipes with default (zero) buffers complete writes only when read. Linux socket buffering hid it. |
| 201 | `d83a964` | core ❌ (hang guard fired after 3 min) | Same test still hung: the `StreamWriter.AutoFlush = true` setter writes the BOM **synchronously**. This led to the ISS-006 deadlock finding. |
| 202 | `b60d645` | cancelled | Superseded by 203. |
| 203 | `8d808a7` | core ✅ 89/89, tray ❌ (expected) | Results of record. |

No test was skipped, disabled or weakened to get green. Two tests were corrected for **test** bugs (the fake service closing too early, and blocking pipe writes in test code); each correction is in its own commit with the reason.

## 4. Red-before-green evidence

Every regression test was also run against the previous implementation (only the test seam applied); see the resolution log in `03-issue-register.md`. Summary: client handshake 2/2 failed, sync/buffer 7 failed, pipe server 3/3 failed, reconnect failed, coordinator failed (6 concurrent calls), double dispose failed.

## 5. Not automated (and why)

| Area | Reason |
|---|---|
| TrayApp UI, tray icon, single instance | Tray does not build (ISS-005) |
| Low-level hooks, `GetLastInputInfo`, foreground process | Need an interactive Windows desktop session |
| Login, attendance, breaks, E2E (T15) | Features not implemented (ISS-002, ISS-003) |
| Live backend contract | No backend/UAT endpoint available to this engagement |
| Sleep, hibernate, lock, multi-user | Need physical/virtual Windows devices (see 11) |
