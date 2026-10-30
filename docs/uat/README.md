# ONEVO Tray App — UAT Readiness Package (`develop`)

Start with **[12 — UAT Readiness Summary](12-uat-readiness-summary.md)**, then **[14 — Final Gap Audit](14-final-uat-gap-audit.md)** (`develop` @ `9a13436`).

| # | Document | Task |
|---|---|---|
| 01 | [Technology Stack & Architecture](01-architecture-report.md) | T01 |
| 02 | [Security Review](02-security-review.md) | T03 |
| 03 | [Issue Register](03-issue-register.md) | T02, T09 |
| 04 | [Test Coverage Matrix](04-test-coverage-matrix.md) | T08 |
| 05 | [Automated Test Execution Report](05-test-execution-report.md) | T08 |
| 06 | [Tray ↔ Backend Integration Report](06-api-integration-report.md) | T04 |
| 07 | [Installer & Upgrade Guide](07-installer-and-upgrade-guide.md) | T12 |
| 08 | [Environment Configuration Guide](08-environment-configuration-guide.md) | T13 |
| 09 | [Troubleshooting Guide](09-troubleshooting-guide.md) | T14 |
| 10 | [Release Notes & Known Issues](10-release-notes-and-known-issues.md) | T14 |
| 11 | [Manual Testing Checklist](11-manual-testing-checklist.md) | T16–T18 |
| 12 | [UAT Readiness Summary](12-uat-readiness-summary.md) | T18 input |
| 13 | [CPU, RAM, Disk & Network](13-performance-report.md) | T10 |
| 14 | [Final UAT Readiness Gap Audit](14-final-uat-gap-audit.md) | Final audit, Go/No-Go |

## Scope and method

- **Baseline:** `develop` @ `a05e7d9` (PR #99).
- **Prior audit:** `develop` already carries an audit/remediation series. AUD-001…036, AGT-01/02/03, SEC-01, T10 and T15 are visible in commit history and `docs/fixes/`; that series' own register is not stored in this repository.
- **What this audit did:**
  - mapped the findings of an earlier audit of `main` to `develop`;
  - reviewed the high-risk paths (attendance and activity accounting, sync outcomes, IPC lifecycle, configuration, packaging);
  - verified each claim in these documents against the code;
  - fixed what was still broken (UAT-01).
- **What it did not do:** a line-by-line review of all ~25,700 lines.

## Keeping this package current

Use the project skills in `.claude/skills/`:
- `uat-docs` updates this package and the issue register;
- `run-tests` builds, runs and reads CI results.
