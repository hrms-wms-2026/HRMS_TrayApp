# 05 — Automated Test Execution Report (T08)

## Pipeline

`.github/workflows/ci.yml` runs on windows-latest with SDK 10.0.300 from `global.json` and the `maui-windows` workload. It triggers on push to `main`/`develop` and on PRs into them:

1. Build Shared, Service and TrayApp (Release).
2. Test the three projects with coverage.
3. Coverage gate (`coverage-thresholds.json`, AUD-025).

`release.yml` runs the same three test projects before building a signed MSIX for a `vX.Y.Z` tag.

## Results of record — PR #100 head `f987d83`

Run: <https://github.com/hrms-wms-2026/HRMS_TrayApp/actions/runs/40864898740> — **success**

| Test project | Result |
|---|---|
| ONEVO.Agent.Shared.Tests | 42/42 passed |
| ONEVO.Agent.Service.Tests | 514/514 passed |
| ONEVO.Agent.TrayApp.Tests | **580/580 passed** (includes the interactive-desktop hook tests and the 12 tests added in this PR) |

The Shared and Service counts were read from the PR's first run on `61e43f9`. Both steps passed again on `f987d83`, which changes no code in those projects.

| Assembly | Line (floor) | Branch (floor) | vs `develop` baseline |
|---|---|---|---|
| Shared | 46.8% (26%) | 87.5% (0%) | = |
| Service | 75.1% (72%) | 68.1% (63%) | = |
| TrayApp | 47.4% (45%) | 26.4% (24%) | +0.4 / +0.1 |

## Run history on this PR

| Run | Commit | Result | Notes |
|---|---|---|---|
| 40864213479 | `61e43f9` | ❌ TrayApp 579/580 | `ActivityCountCollectorTests.Hooks_CountInput_OnTheirOwnThread_WithoutTheCallerPumpingMessages` failed: the first UAT-01 version dropped a sub-second final snapshot together with its input counts. Real regression, fixed in `f987d83` |
| 40864446548 | `23dabe1` (docs) | superseded | Same code as above |
| 40864898740 | `f987d83` | ✅ | Results of record |

No test was skipped, disabled or changed to get green.

## Baseline — `develop` @ `a05e7d9`

CI run 197: success. Coverage as in the table above, except TrayApp at 47% / 26.3%.

## Local runs

| Environment | Result |
|---|---|
| Linux, SDK 10.0.113 | Shared 42/42. Service 379/514: the 135 failures are `PlatformNotSupportedException` (DPAPI, ACL, SystemEvents) and Windows path semantics, i.e. not product failures (UAT-06). TrayApp cannot build (MAUI Windows). The new tracker tests were compiled standalone: 8/8 |
| Windows | Use the `run-tests` skill (`.claude/skills/run-tests/SKILL.md`) |
