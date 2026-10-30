---
name: run-tests
description: Build and run the ONEVO Tray App test suites (Shared, Service, TrayApp), read the Windows CI result of a branch or PR, and decide where a new regression test belongs. Use when asked to run, check or add tests, verify a fix, or investigate a red CI run in this repository.
---

# Run and check the ONEVO Tray App tests

## Facts about this repo

- **SDK:** `global.json` pins **10.0.300** with `rollForward: disable`; any other SDK refuses to build. The TrayApp also needs the MAUI workload: `dotnet workload install maui-windows`.
- **Test projects and what they reference:**

  | Project | Target | Builds on |
  |---|---|---|
  | `tests/ONEVO.Agent.Shared.Tests` | `net10.0` | any OS |
  | `tests/ONEVO.Agent.Service.Tests` | `net10.0-windows` | Windows (on Linux with `-p:EnableWindowsTargeting=true`, but ~135 tests then fail with `PlatformNotSupportedException` / Windows paths; not product failures, see UAT-06) |
  | `tests/ONEVO.Agent.TrayApp.Tests` | `net10.0-windows10.0.19041.0`, references the MAUI app | Windows only |

- Tests tagged `[Trait("Category", "InteractiveDesktop")]` inject real input and need a desktop session. CI's windows-latest runner has one, so they run there.
- **Source of truth:** Windows CI (`.github/workflows/ci.yml`). It runs on push to `main`/`develop` and on PRs into them, with a coverage gate (`scripts/check-coverage.ps1`, floors in `coverage-thresholds.json`). A pushed topic branch gets CI only once a PR into `develop`/`main` exists.

## Run locally (Windows)

Use a checkout path of 103 characters or less, otherwise the TrayApp build fails with `MSB3030` (see README).

```powershell
dotnet test tests/ONEVO.Agent.Shared.Tests --configuration Release
dotnet test tests/ONEVO.Agent.Service.Tests --configuration Release
dotnet test tests/ONEVO.Agent.TrayApp.Tests --configuration Release --filter "Category!=InteractiveDesktop"
```

Stop any running agent first (`Stop-Process -Name ONEVO.Agent.Service,ONEVO.Agent.TrayApp -Force`), or DLL copies fail with `MSB3027`.

Coverage like CI:

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests -c Release --collect:"XPlat Code Coverage" --results-directory coverage/ONEVO.Agent.Service.Tests
./scripts/check-coverage.ps1 -ResultsRoot coverage
```

## Run locally (Linux / no MAUI)

- Shared tests run normally.
- Service tests: add `-p:EnableWindowsTargeting=true` and compare **failure reasons**, not counts. Group them first:

  ```bash
  dotnet test tests/ONEVO.Agent.Service.Tests -p:EnableWindowsTargeting=true 2>&1 \
    | grep -A1 "Error Message" | grep -v -e "Error Message" -e '^--$' | sort | uniq -c | sort -rn
  ```

  Only failures other than `PlatformNotSupportedException` / Windows-path assertions point at real problems.
- TrayApp tests cannot build. For pure logic (e.g. `Collectors/ActivityIntervalTracker.cs`), compile the source file plus its test file into a scratch `net10.0` xunit project. Add the test project's global usings (`Xunit`, `ONEVO.Agent.TrayApp.Collectors`) to that project. Run it, then rely on CI for the rest.
- If `global.json`'s SDK is not installable, copy the repo to a scratch folder and relax `global.json` **there only**. Never commit an SDK change for a local run.

## Read CI results

1. Get the PR's check runs (GitHub MCP `pull_request_read` → `get_check_runs`), or the workflow runs for the branch.
2. For a failed job, fetch the log (`get_job_logs`, `tail_lines` ≈ 120). Find the `Failed <TestName>` lines and the `Error Message`.
3. The coverage table is printed by the "Coverage gate" step. Copy its numbers into `docs/uat/05-test-execution-report.md` with the run URL.
4. Report counts exactly as printed (`Passed!  - Failed: 0, Passed: N`). Do not claim a pass for a step that was skipped or cancelled.

## Where a new test goes

| Code under test | Test project / folder |
|---|---|
| `ONEVO.Agent.Shared/**` | `tests/ONEVO.Agent.Shared.Tests` |
| Service sync / buffer / lifecycle / IPC / security | `tests/ONEVO.Agent.Service.Tests/{Sync,Buffer,Lifecycle,IPC,Security}` |
| Full workday flows | `tests/ONEVO.Agent.Service.Tests/EndToEnd` |
| Tray collectors / services / view models | `tests/ONEVO.Agent.TrayApp.Tests/{Collectors,Services,ViewModels}`; fakes in `Fakes/` |

TrayApp internals are visible to the test project (`InternalsVisibleTo`). Prefer an `internal` constructor that injects a clock or an OS call over a test that needs the real OS.

## Rules

- A regression test must fail on the old code and pass on the fix. Say how you showed that, or why it could not be shown.
- Never skip, disable or quarantine a test to get green, and never lower a coverage floor without a measured reason in the PR.
- Never use real employee data. Use synthetic ids and tokens such as `test-device-token`.
- A red CI on your own PR is your work: read the log, root-cause it, and push one validated fix.
