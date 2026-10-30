# 05 — Automated Test Execution Report (T08)

## Pipeline

`.github/workflows/ci.yml` runs on windows-latest with SDK 10.0.300 from `global.json` and the `maui-windows` workload. It triggers on push to `main`/`develop` and on PRs into them:

1. Build Shared, Service and TrayApp (Release).
2. Test the three projects with coverage.
3. Coverage gate (`coverage-thresholds.json`, AUD-025).

`release.yml` runs the same three test projects before building a signed MSIX for a `vX.Y.Z` tag.

## PR #105 (`214366c`) — UAT-22 last warnings

Run 234: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41026270607> — **success**. Shared 42/42, Service 559/559, TrayApp 582/582, 0 skipped. **Build: 0 warnings in all three projects** (TrayApp was 49 on `develop` before #103). Isolation guard: self-check caught the planted file; real folders unchanged.

## PR #104 (`eeb5b43`) — UAT-20 isolation guard

Run 231: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41018122916> — **success**. Shared 42/42, Service 559/559, TrayApp 582/582, 0 skipped. TrayApp build 5 warnings (UAT-22, unchanged).

Guard output, as printed:
- Self-check: `Test run touched real agent data - new or changed: F|D:\a\_temp\isolation-self-check\planted.txt|…`, so the script exits 1 on a planted write.
- Snapshot: `Recorded 0 entries under: C:\ProgramData\ONEVO, C:\Users\runneradmin\AppData\Local\ONEVO`.
- Compare after all three test projects: `Real agent data folders unchanged`. Today's suites do not touch the real folders.

## PR #103 head `7895578` — steps 1–2 (UAT-17 part 2, UAT-02, UAT-22)

Run 228: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41015247939> — **success**. Shared 42/42, Service 559/559, TrayApp 582/582, 0 skipped; coverage Shared 46.8%, Service 76%, TrayApp 47.3% line (all above floors).

TrayApp build: **5 warnings** (all `XC0023` on the two opted-out page bindings), down from 49 `XC0025`. Shared and Service: 0.

| Run | Commit | Result | Notes |
|---|---|---|---|
| 226 | `a15e628` | ✅ but 52 warnings | Source compilation without `x:DataType`: 47 × `XC0022` + 5 × `XC0023` |
| 227 | `76a2b56` | ❌ build | `XC0045` errors on the 2 page bindings without opt-out (checked against the DataTemplate type); otherwise 0 warnings |
| 228 | `7895578` | ✅ | Results of record |

## PR #103 (`139c645`) — UAT-17 part 2

Run 224: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41012511847> — **success**.

| Test project | Result |
|---|---|
| ONEVO.Agent.Shared.Tests | `Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42` |
| ONEVO.Agent.Service.Tests | `Passed! - Failed: 0, Passed: 559, Skipped: 0, Total: 559` (+15 in `AgentWorkerTests`: 2 facts + 13 theory cases) |
| ONEVO.Agent.TrayApp.Tests | `Passed! - Failed: 0, Passed: 582, Skipped: 0, Total: 582` |

Coverage: Service 76% line / 68.6% branch (floors 72% / 63%); Shared and TrayApp unchanged. Build: Service 0 warnings; TrayApp 49 `XC0025` (UAT-22).

## PR #102 (`f06743a`) — AUD-008 / UAT-17 part 1

Run 221: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/41004208690> — **success**.

| Test project | Result |
|---|---|
| ONEVO.Agent.Shared.Tests | `Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42` |
| ONEVO.Agent.Service.Tests | `Passed! - Failed: 0, Passed: 544, Skipped: 0, Total: 544` (+26: `NamedPipeServerDispatchTests` 4 facts + 22 theory cases) |
| ONEVO.Agent.TrayApp.Tests | `Passed! - Failed: 0, Passed: 582, Skipped: 0, Total: 582` |

Coverage: Shared 46.8% / 87.5%; Service 75.3% / 68.2% (+0.1 line); TrayApp 47.4% / 26.4%; all above floors. Build: Shared/Service 0 warnings, TrayApp 49 `XC0025` (UAT-22, unchanged).

The regression tests were not run against the old code: no SDK in the session (see "Local run for this audit" below). That they fail there follows from the code: before the change the read loop awaited every handler, so the second send or the first read times out (dispatch test 1), and a handler exception ended the connection (test 4).

## Results of record — `develop` head `9a13436` (final gap audit, 2026-10-30)

Run 220: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/40999925848> — **success** (job `build-and-test`, windows-latest). Counts as printed in the job log:

| Test project | Result |
|---|---|
| ONEVO.Agent.Shared.Tests | `Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42` |
| ONEVO.Agent.Service.Tests | `Passed! - Failed: 0, Passed: 518, Skipped: 0, Total: 518` |
| ONEVO.Agent.TrayApp.Tests | `Passed! - Failed: 0, Passed: 582, Skipped: 0, Total: 582` |

Coverage gate: Shared 46.8% line (floor 26%) / 87.5% branch (floor 0%); Service 75.2% (72%) / 68.2% (63%); TrayApp 47.4% (45%) / 26.4% (24%), all `ok`.

Build: Shared and Service `0 Warning(s)`; TrayApp `49 Warning(s)`, all XamlC `XC0025` (UAT-22). `0 Error(s)` everywhere.

Local run for this audit: not possible. The environment's network policy refused the .NET SDK download (`builds.dotnet.microsoft.com`), so no test was run outside CI.

## Earlier result of record — PR #100 head `86aa632`

Run: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/40869661955> — **success**

| Test project | Result |
|---|---|
| ONEVO.Agent.Shared.Tests | 42/42 passed |
| ONEVO.Agent.Service.Tests | **518/518 passed** (includes the 4 UAT-13 retention tests) |
| ONEVO.Agent.TrayApp.Tests | **580/580 passed** (includes the interactive-desktop hook tests and the 12 UAT-01 tests) |

| Assembly | Line (floor) | Branch (floor) | vs `develop` baseline |
|---|---|---|---|
| Shared | 46.8% (26%) | 87.5% (0%) | = |
| Service | 75.2% (72%) | 68.2% (63%) | +0.1 / +0.1 |
| TrayApp | 47.4% (45%) | 26.4% (24%) | +0.4 / +0.1 |

## Run history on this PR

| Run | Commit | Result | Notes |
|---|---|---|---|
| 40864213479 | `61e43f9` | ❌ TrayApp 579/580 | `ActivityCountCollectorTests.Hooks_CountInput_OnTheirOwnThread_WithoutTheCallerPumpingMessages` failed: the first UAT-01 version dropped a sub-second final snapshot together with its input counts. Real regression, fixed in `f987d83` |
| 40864446548 | `23dabe1` (docs) | superseded | Same code as above |
| 40864898740 | `f987d83` | ✅ | |
| 40868376706 | `a8dab05` | ✅ | UAT-13 fix |
| 40868481969 | `2c64edf` (docs) | ❌ Service 517/518 | `ActivitySyncFlushRequestTests.WithoutARequest_RowsWaitForTheIngestInterval` expected 0 uploads, got 1. Timing race in the test: it assumed the start-up flush had read the queue within a fixed 200 ms. The same code had passed on `a8dab05` |
| 40869661955 | `86aa632` | ✅ | Results of record |

No test was skipped or disabled. One existing test was changed (`86aa632`): `WithoutARequest_RowsWaitForTheIngestInterval` now waits for the start-up flush's own log line instead of a fixed delay. Its assertion (no upload without a request) is unchanged.

## Baseline — `develop` @ `a05e7d9`

CI run 197: success. Coverage as in the table above, except TrayApp at 47% / 26.3%.

## Local runs

| Environment | Result |
|---|---|
| Linux, SDK 10.0.113 | Shared 42/42. Service 379/514: the 135 failures are `PlatformNotSupportedException` (DPAPI, ACL, SystemEvents) and Windows path semantics, i.e. not product failures (UAT-06). TrayApp cannot build (MAUI Windows). The new tracker tests were compiled standalone: 8/8 |
| Windows | Use the `run-tests` skill (`.claude/skills/run-tests/SKILL.md`) |
