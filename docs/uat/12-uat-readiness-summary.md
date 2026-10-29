# 12 — UAT Readiness Summary

**Recommendation: GO for manual Windows compatibility testing (T16). NO-GO for UAT distribution to employees** until the installer ships the Agent Service and starts the tray at logon (UAT-07, UAT-08).

The application on `develop` is functionally complete for UAT scope:
- onboarding / activation;
- clock-in/out and breaks;
- face check;
- activity, app-usage and device-state monitoring;
- offline queue, notifications, daily summary.

It is well covered by automated tests, including a workday E2E with a backend outage. The remaining blockers are packaging and policy decisions, not code defects.

## Task status

| Task | Status | Evidence |
|---|---|---|
| T01 Architecture | Done | 01 |
| T02 Static audit | Done (targeted re-audit of `develop` on top of the AUD series) | 03 |
| T03 Security | Done; open items are device/backend checks | 02 |
| T04 API contract | Done; live UAT backend check pending | 06 |
| T05 Offline sync | Done on `develop` (AGT-01/02, AUD-004) | 06, tests |
| T06 Calculation correctness | **UAT-01 fixed here**; attendance accounting reviewed (UAT-04 limitation) | 03 |
| T07 Lifecycle | Done in code (AUD-001/005/006/014); **UAT-08 autostart open** | 01 |
| T08 Tests & CI | Done; CI + coverage gate | 04, 05 |
| T09 Defect remediation | UAT-01 (+ regression caught by CI) | PR #100 |
| T10 Performance | Idle budgets done; active budgets pending a device run | 13 |
| T11 Diagnostics | Done (AUD-018); UAT-03 policy decision | 09 |
| T12 Installer | **Blocked: UAT-07, UAT-08** | 07 |
| T13 Configuration | Done; confirm `ApiBaseUrl` per environment (UAT-11) | 08 |
| T14 Documentation | Done (this package) | README |
| T15 E2E | In-process workday E2E exists (`WorkdayOfflineRecoveryFlowTests`) | 04 |
| T16–T18 | Not started; checklist ready | 11 |

## Escalations (assignment §7)

| Category | Item | Owner |
|---|---|---|
| Change that may break installations | UAT-07 / UAT-08: how the Service and tray autostart are installed | Release / architecture owner |
| Monitoring behaviour vs. policy | UAT-03: activity counts persisted in `tray.log` | Monitoring-policy owner |
| Accounting | UAT-01 changes the values reported in activity snapshots (more accurate, lower totals on fragmented days) | Product owner (inform report consumers) |

## Path to UAT Go

1. Merge PR #100 after review (CI green).
2. Decide and implement Service + tray-autostart packaging (UAT-07/08); sign with the production certificate (T17).
3. Run the manual checklist (11) on Windows 10 and 11 devices.
4. Measure active-monitoring footprint (13).
5. Pilot (T18) and signed go/no-go.
