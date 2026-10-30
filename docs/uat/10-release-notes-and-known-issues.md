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

## PR #105

| Change | Issue |
|---|---|
| Legal consent "View" and Work Location option tap call their commands from code-behind; last 5 XamlC warnings removed | UAT-22 |

## PR #104 (step 3)

| Change | Issue |
|---|---|
| CI fails when a test run creates, changes or deletes anything under the real `%ProgramData%\ONEVO` or `%LOCALAPPDATA%\ONEVO` | UAT-20 |

## PR #103, step 2

| Change | Issue |
|---|---|
| Tray day idle counted once (DeviceState only) | UAT-02 |
| XAML bindings with `Source={x:Reference}` compiled; TrayApp build warnings 49 → 5 (CI run 228) | UAT-22 |

## PR #103

| Change | Issue |
|---|---|
| Read-only tray requests (daily tasks, pending location check, face reference, consent status, update check, notification ack) give up on a hung backend after the time the tray waits (8–30 s) and reply with a failure, instead of waiting through every retry | UAT-17 part 2 |

## PR #102 (merged)

| Change | Issue |
|---|---|
| The Service handles independent tray requests (daily tasks, location change, consent, notification ack, update check, …) off the pipe read loop, at most 4 at a time per tray. A slow backend call no longer holds back a clock action sent after it. Stateful messages keep their order. A failing request no longer drops the tray's connection | AUD-008, UAT-17 (part 1) |
| Final UAT gap audit documents (14) and register updates | — |

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
| UAT-04 | — | Idle end detected at 60 s sample resolution | Accepted limitation |
| UAT-05 | P2 | GitHub default branch `main` is stale | Use `develop`; release PR `develop → main` pending |
| UAT-06 | P3 | Some Service tests are Windows-only but not marked | Run tests on Windows |
| UAT-09 | P3 | Credential confidentiality relies on the folder ACL | Device check (11 §F) |
| UAT-10 | Info | Production signing certificate needed | T17 |
| UAT-11 | P1 | No UAT configuration; default `ApiBaseUrl` is the dev host | Override with `Agent__ApiBaseUrl` |
| UAT-15 | P2 | Daily summary task list empty against backend `development` (endpoint not merged) | None |
| UAT-16 | P2 | App-usage/device-state/meeting rows can be duplicated after a lost response | None; report caveat |
| UAT-17 | P1 | Parts 1–2 done: independent requests no longer block; read-only calls have a budget. Remaining: writing calls have no budget (D-15); a slow face enrol can still delay later messages | Retry the action |
| UAT-18 | P0 | Consent/policy/time-off gates not enforced | Do not enrol UAT users before the fix |
| UAT-19 | P3 | Windows version declarations inconsistent | None |
| AUD-020 | P1 | QuestPDF Community licence eligibility not decided | Legal decision |
| UAT-21 | P2 | No release tag/branch/RC yet | — |
| UAT-12 | P1 | Face photos kept unencrypted in SQLite and retained after upload | Retention/privacy decision pending (13 §Storage) |
