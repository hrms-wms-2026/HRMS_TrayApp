# ONEVO Tray App — UAT Readiness Package

Start with **[12 — UAT Readiness Summary](12-uat-readiness-summary.md)** (recommendation: **No-Go**).

| # | Document | Task |
|---|---|---|
| 01 | [Technology Stack & Architecture Report](01-architecture-report.md) | T01 |
| 02 | [Security Review Report](02-security-review.md) | T03 |
| 03 | [Issue Register](03-issue-register.md) (severity, status, resolution log) | T02, T09 |
| 04 | [Test Coverage Matrix](04-test-coverage-matrix.md) | T08 |
| 05 | [Automated Test Execution Report](05-automated-test-execution-report.md) | T08 |
| 06 | [Tray ↔ Backend API Integration Report](06-api-integration-report.md) | T04 |
| 07 | [Installer & Upgrade Guide](07-installer-and-upgrade-guide.md) | T12 |
| 08 | [Environment Configuration Guide](08-environment-configuration-guide.md) | T13 |
| 09 | [Troubleshooting Guide](09-troubleshooting-guide.md) | T14 |
| 10 | [Release Notes & Known Issues](10-release-notes-and-known-issues.md) | T14 |
| 11 | [Remaining Manual Testing Checklist](11-manual-testing-checklist.md) | T16 |
| 12 | [UAT Readiness Summary](12-uat-readiness-summary.md) | T18 input |
| 13 | [CPU, RAM, Disk & Network Report](13-performance-report.md) | T10 |

## Running the tests

Prerequisite: .NET SDK 10.0.300 (pinned in `global.json`).

```bash
dotnet test tests/ONEVO.Agent.Shared.Tests
```

```bash
dotnet test tests/ONEVO.Agent.Service.Tests
```

```bash
dotnet test tests/ONEVO.Agent.TrayApp.Tests
```

On non-Windows hosts add `-p:EnableWindowsTargeting=true`; Windows-only tests are skipped there and run in CI.
