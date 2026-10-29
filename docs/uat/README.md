# UAT Readiness — `develop` (audit of `a05e7d9`)

| Document | Contents |
|---|---|
| [issue-register.md](issue-register.md) | Findings for `develop` (UAT-01…), status, evidence; mapping of earlier findings to existing AUD fixes |
| [test-report.md](test-report.md) | Automated test results and how to run them |
| [manual-testing-checklist.md](manual-testing-checklist.md) | Device testing still required (T16–T18) |

## Scope and method

- **Baseline:** `develop` at `a05e7d9` (PR #99), 326 commits ahead of `main` (`5cca93e`, "first commit").
- **Prior audit:** `develop` has already been through an audit/remediation series. The AUD-001…AUD-036, AGT-01/02/03, SEC-01, T10 and T15 work is visible in commit history and in `docs/fixes/2026-10-12-agt-sec-remediation-plan.md`. That audit's register is not stored in this repository.
- **What this audit did:**
  - Re-checked every defect found in an earlier pass over `main` against `develop`'s code.
  - Reviewed the highest-risk paths: attendance accounting (`PresenceSession`), activity accounting (collectors), sync outcome handling, IPC lifecycle and reconnect, and config validation.
  - Fixed the one defect that was still present.
- **What it did not do:** a line-by-line review of all ~25,700 lines of source.

## Readiness summary

| Area | State on `develop` |
|---|---|
| Build | Shared, Service and TrayApp build on Windows CI (run 197 green) |
| Automated tests | Shared + Service + TrayApp suites with a coverage gate; T15 in-process workday E2E with a backend outage |
| Offline sync / data integrity | Durable SQLite queue with peek/ack, per-record quarantine (payload kept), owner-bound rows, backoff, bounded stop flush |
| Attendance accounting | Service-side `PresenceSession` with idle/break/gap handling and restart resume (AUD-001) |
| Activity accounting | **UAT-01 fixed in this PR**; previously over-reported active time |
| Security | Data-folder ACL (AUD-003), IPC client verifier, bounded line reader, https-only API URL, dev-bypass flags refused outside Development (AUD-013) |
| Lifecycle | Single tray instance, reconnect, sleep/hibernate via SCM, collectors stop on lock (AUD-005/006/014) |
| Observability | Rolling `tray.log` / `service.log`, correlation IDs, crash capture (AUD-018) |

**Recommendation:** ready for **manual Windows compatibility testing (T16)** once this PR's CI is green. A UAT **Go** still needs:
- T16 device results ([checklist](manual-testing-checklist.md))
- a signed installer (T17)
- product decisions on the open items in the register (UAT-03 logging policy)
- pilot sign-off (T18)

Nothing in this PR merges or deploys anything by itself.
