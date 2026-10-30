# 10 — Release Notes & Known Issues

## This PR (#100, into `develop`)

| Change | Issue |
|---|---|
| Activity snapshots report the time they actually cover. A partial interval (break, lock, reconnect) no longer counts as a full minute, and every idle span inside an interval is counted. Wire format unchanged; `active_seconds`/`idle_seconds` values are now accurate. | UAT-01 |
| Input counted in a sub-second final snapshot is kept (regression caught by CI in the first version of the fix). | UAT-01 follow-up |
| Delivered rows are deleted 24 h after queueing and freed space is returned with incremental auto-vacuum. Device storage stays ≈1–2 MB instead of growing to 100 MB, and there is no full `VACUUM` under the buffer lock. | UAT-13 |
| UAT documentation package (`docs/uat/`) and project skills for maintaining it (`.claude/skills/`). | — |

**Expected effect on reports:** activity `active_seconds` totals drop slightly on days with many breaks, locks or reconnects. Days with idle stretches that ended before a snapshot show more idle time. Attendance work time (from `PresenceSession`) is not affected.

## PR #101

| Change | Issue |
|---|---|
| 45 unused images removed; the remaining PNGs are re-encoded losslessly. MSIX 48.25 → 43.85 MB. No visual change | UAT-14 |

## Already on `develop` before PR #100 (summary from commit history)

- Durable queue with per-record outcomes and quarantine (AGT-01/02).
- Open session restored after a Service restart (AUD-001).
- Data folder ACL (AUD-003).
- Owner-bound queue rows (AUD-004).
- Offline tray state and single instance (AUD-005/006).
- Clock action time budget and recovery (AUD-008/009/012).
- Dev flags guarded outside Development (AUD-013).
- Lock, sleep and hibernate handling (AUD-014).
- Response disposal (AUD-015).
- Logging with correlation ids (AUD-018).
- GPS not persisted (AUD-019).
- Dependency servicing (AUD-022).
- Retry on timeouts and 408 (AUD-023).
- Coverage gate (AUD-025).
- Break race fix (AUD-035).
- Idle footprint budgets (T10).
- In-process workday E2E (T15).

## Known issues

| ID | P | Summary | Workaround |
|---|---|---|---|
| UAT-07 | P1 | Released MSIX does not install the Agent Service | Install the Service with `scripts/install-service.ps1` (07) |
| UAT-08 | P1 | Tray does not start at logon | Employee opens the tray after every sign-in; time before that counts as idle |
| UAT-03 | P3 | Activity counts are written to `tray.log` | Policy decision pending |
| UAT-02 | P3 | Tray day-metrics idle accumulated twice (value unused) | None needed today |
| UAT-04 | — | Idle end detected at 60 s sample resolution | Accepted limitation |
| UAT-05 | P2 | GitHub default branch `main` is stale | Use `develop`; release PR `develop → main` pending |
| UAT-06 | P3 | Some Service tests are Windows-only but not marked | Run tests on Windows |
| UAT-09 | P3 | Credential confidentiality relies on the folder ACL | Device check (11 §F) |
| UAT-10 | Info | Production signing certificate needed | T17 |
| UAT-11 | P3 | Confirm `ApiBaseUrl` per environment | Override with `Agent__ApiBaseUrl` |
| UAT-12 | P1 | Face photos kept unencrypted in SQLite and retained after upload | Retention/privacy decision pending (13 §Storage) |
