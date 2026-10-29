# Automated Test Report — `develop`

## Pipeline
`.github/workflows/ci.yml` (windows-latest, SDK from `global.json` 10.0.300, `maui-windows` workload) runs on push to `main`/`develop` and on PRs into them:
1. Build Shared, Service and TrayApp (Release).
2. Test Shared, Service and TrayApp, collecting coverage.
3. Coverage gate against `coverage-thresholds.json` (AUD-025).

## Baseline — `develop` @ `a05e7d9`, CI run 197: success

| Assembly | Line coverage (floor) | Branch coverage (floor) |
|---|---|---|
| ONEVO.Agent.Shared | 46.8% (26%) | 87.5% (0%) |
| ONEVO.Agent.Service | 75.1% (72%) | 68.1% (63%) |
| ONEVO.Agent.TrayApp | 47% (45%) | 26.3% (24%) |

## This PR (#100)

| Added test class | Tests | Where it ran |
|---|---|---|
| `Collectors/ActivityIntervalTrackerTests` | 8 | Standalone on Linux (SDK 10.0.113): **8/8 passed**; Windows CI on PR #100 |
| `Collectors/ActivityCountCollectorAccountingTests` | 4 | Windows CI on PR #100 (the tray test project references the MAUI app, so it builds only on Windows) |

The PR's CI result is the authoritative record. Check the `build-and-test` run on PR #100 before merging.

## Running locally
On Windows with SDK 10.0.300 and the `maui-windows` workload:

```bash
dotnet test tests/ONEVO.Agent.Shared.Tests
```

```bash
dotnet test tests/ONEVO.Agent.Service.Tests
```

```bash
dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "Category!=InteractiveDesktop"
```

The `InteractiveDesktop` tests inject real input and need a desktop session.

Off Windows, Shared tests pass (42/42). About 135 Service tests fail with `PlatformNotSupportedException` or Windows path semantics (UAT-06), and the TrayApp tests cannot build.
