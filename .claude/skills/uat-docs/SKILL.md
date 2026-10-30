---
name: uat-docs
description: Maintain the ONEVO Tray App UAT documentation package in docs/uat (issue register, readiness summary, test report, release notes, guides). Use when a fix, finding, test run, release or decision should be recorded, or when asked to create, update or review the UAT/handover documents.
---

# Maintain the UAT documentation package (`docs/uat`)

## Package layout

| File | Update when |
|---|---|
| `README.md` | Documents are added or renamed; scope changes |
| `01-architecture-report.md` | Components, hosted services, storage paths or lifecycle behaviour change |
| `02-security-review.md` | A security control is added or changed, or a security finding is raised or closed |
| `03-issue-register.md` | **Every** finding, fix, verification or decision |
| `04-test-coverage-matrix.md` | Test classes are added for a new behaviour; coverage floors change |
| `05-test-execution-report.md` | A CI run becomes the result of record (link it) |
| `06-api-integration-report.md` | `Api/AgentApiRoutes.cs`, response handling or IPC message families change |
| `07-installer-and-upgrade-guide.md` | Packaging, install scripts, release workflow or upgrade behaviour change |
| `08-environment-configuration-guide.md` | `AgentOptions`, `appsettings*.json` or validation rules change |
| `09-troubleshooting-guide.md` | A new failure mode, log line or recovery tool appears |
| `10-release-notes-and-known-issues.md` | A PR lands or an issue opens/closes |
| `11-manual-testing-checklist.md` | A behaviour needs device verification |
| `12-uat-readiness-summary.md` | Task status, blockers or the Go/No-Go recommendation change |
| `13-performance-report.md` | `footprint-budgets.json` or periodic work changes |

## Issue register rules (`03-issue-register.md`)

- **IDs:**
  - `UAT-NN`: next free number, never reused.
  - `AUD-NNN` / `AGT-NN`: identifiers from the earlier audit series. Reference them, never renumber them.
- **Priority:** P0 (blocks UAT / data loss / security breach) · P1 (wrong results or broken workflow) · P2 (degraded, workaround exists) · P3 (minor / hygiene) · Info / — (observation).
- **Classification:**
  - **Verified**: reproduced by a test, CI run or experiment.
  - **Confirmed (code)**: follows from the code, not executed.
  - **Observation**: design limitation or decision.
- **Status:** Open · Escalated (needs a human/product/backend decision) · Fixed in PR #N (commits) · Fixed – verified · Accepted limitation · Won't fix.
- **Each detail entry contains:**
  - files / code references;
  - actual vs expected behaviour;
  - evidence or reproduction;
  - root cause;
  - fix or recommendation;
  - tests added;
  - verification result;
  - remaining manual verification.
- Add a row to the summary table **and** a `### UAT-NN` detail section. When an issue is fixed, also update `10-…` and, if it was a blocker, `12-…`.
- **Escalate** (status Escalated, plus a row in `12-…` §Escalations):
  - employee data loss or wrong attendance accounting;
  - cross-tenant access;
  - credential exposure;
  - monitoring beyond policy;
  - unrecoverable sync;
  - backend contract changes;
  - anything that can break existing installations.

## Writing rules

1. **Verify every claim in the code before writing it.** Open the file, or grep for the route, option or constant. Earlier drafts of this package contained claims that turned out wrong on inspection; for example, development portal origins are honoured only in Development. A claim not verified is written as "not verified".
2. **Distinguish what ran from what is inferred.** Test counts and coverage come from a named CI run with its URL. Never write "tests pass" without one.
3. **Cross-references** use the document number and section letter, e.g. `(11 §F)`. After reordering sections, grep `§` across `docs/uat` and fix every reference.
4. **No secrets, tokens, personal data, real employee names or monitoring content** in any document. Use placeholders.
5. **Keep findings for `develop` separate from findings for other branches.** `main` (`5cca93e`) is a stale scaffold; findings about it do not apply to `develop`.
6. **Prefer short tables** for registers and matrices. Put explanations in the detail sections.

## Workflow for a fix

1. Fix in its own commit, with a regression test (see the `run-tests` skill).
2. Update `03` (status, commits, tests, verification), `10` (release notes), and `05` once CI is green (run URL, counts, coverage).
3. If behaviour on a device must be checked, add or update an item in `11`.
4. Re-read `12` and adjust the task table and the recommendation if a blocker moved.
5. Commit the docs separately from code (`docs(uat): …`).
