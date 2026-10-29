# 12 — UAT Readiness Summary

**Recommendation: NO-GO for UAT.**
The Tray App cannot be built into an executable, and the core workflows UAT is meant to exercise (login, attendance, breaks, activity upload) are not implemented or not connected. These are scope/architecture gaps, not defects that can be fixed by hardening. Reviewer approval is required on every item below; nothing has been merged or deployed.

## 1. Task status

| Task | Status | Evidence / remaining |
|---|---|---|
| T01 Architecture audit | Done | 01 |
| T02 Static code & dependency audit | Done | 03 (35 issues), 01 §7 |
| T03 Security review | Done — 2 fixed, 3 escalated | 02 |
| T04 API contract audit | Done — contract tests added; no live backend | 06 |
| T05 Offline sync & integrity | Partial — loss paths fixed; **durable queue + idempotency escalated** | ISS-004, ISS-009 |
| T06 Attendance & monitoring calculation | Partial — activity accounting fixed & unit-tested; **attendance not implemented** | ISS-003, ISS-014 |
| T07 Windows lifecycle | Partial — reconnect, exit crash fixed; sleep/lock/single-instance need a buildable tray + devices | ISS-012, 016 |
| T08 Tests & CI | Done — Windows CI, 89 tests, hang guards, versioned service artifact | 04, 05 |
| T09 Critical defect remediation | Done for every defect fixable without a product decision (15 issues fixed, each with regression tests) | 03 resolution log |
| T10 Performance | Partial — component measurements; device measurements blocked | 13 |
| T11 Logging & diagnostics | Reviewed; no changes — tray has no persistent logging (ISS-025) | 09 |
| T12 Installer | Blocked — no installer, no tray binary | 07 |
| T13 Configuration | Done — https enforced, no secrets/localhost in artifacts, UAT URL not provided | 08 |
| T14 Documentation | Done (this package) | 00–13 |
| T15 E2E automation | Blocked — login/attendance absent | 04 |
| T16–T18 Human tasks | Not started — checklist prepared | 11 |

## 2. Test results

Windows CI run 203 (commit `8d808a7`): **89/89 passed** (Shared 17, Service 55, Tray logic 17), including Windows-only ACL, DPAPI and production-pipe tests. TrayApp build job fails by design (no binary). Details: 05.

## 3. Available build / version

- Service: CI artifact `onevo-agent-service-1.0.0-ci.<run>` (framework-dependent, win-x64, unsigned).
- Tray: **none**.
- Installer: **none**.

## 4. Escalations (per assignment §7)

| # | Escalation category | Issue | Decision needed from |
|---|---|---|---|
| 1 | Employee data loss | ISS-004 — every service restart/upgrade/reboot loses unsent activity | Engineering lead (storage design) |
| 2 | Incorrect accounting | ISS-009 — duplicates after lost responses; ISS-022 — multi-user devices attribute activity to one identity | Backend team (contract change) |
| 3 | Unrecoverable sync | ISS-001/002 — no enrollment, nothing uploads | Product + backend |
| 4 | Monitoring policy | ISS-034 — employee can exit the tray; ISS-003 — break handling absent | HR/policy owner |
| 5 | Security | ISS-011 — any local process can talk to (or impersonate) the service pipe | Security reviewer |
| 6 | Breaking existing installations | ISS-005 — making the tray buildable requires MAUI Windows scaffolding or re-platforming | Architecture owner |

## 5. Path to a Go decision

1. Decide the tray platform and make it build (ISS-005); add a single-instance guard and hook thread (ISS-012, ISS-016).
2. Agree backend contracts: enrollment/token refresh, idempotency key, attendance endpoints (ISS-002, 003, 009).
3. Wire the pipeline and add the durable queue (ISS-001, ISS-004).
4. Build and sign an installer (T12, T17).
5. Re-run this audit's CI, then the manual checklist (11) on physical devices, then a pilot (T18).
