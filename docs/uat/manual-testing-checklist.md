# Manual Testing Checklist (T16 → T18)

Use dedicated test devices and synthetic test accounts only. Never use production employee data.

## A. Install & lifecycle
- [ ] Fresh Windows 10 22H2 and Windows 11, standard user: MSIX installs, Service starts automatically, tray starts at logon.
- [ ] Start a second tray instance in the same session → only one stays running.
- [ ] Restart the Service while the tray runs → tray shows Offline, then reconnects without user action.
- [ ] Kill the tray from Task Manager while clocked in → reopen → session state restored from the Service.
- [ ] Upgrade from the previous build → enrollment kept, pending queue rows still uploaded.

## B. Activity accounting (UAT-01)
- [ ] Clock in, type for 60 s → snapshot ≈ 60 s active (backend activity record).
- [ ] 40 s without input, then 20 s typing → snapshot ≈ 40 idle / 20 active.
- [ ] Start a break 10 s after a snapshot → final snapshot covers ≈ 10 s, not 60.
- [ ] Lock the screen 10 times in 10 minutes → total active+idle ≤ wall time (no phantom minutes).
- [ ] Sleep 30 min → no snapshot covers more than one interval.

## C. Attendance
- [ ] Clock-in/out, break start/end, double-click protection, cross-midnight shift.
- [ ] Idle beyond the policy threshold → idle pause recorded; work time excludes it.
- [ ] Lock / sleep / hibernate during a session → time excluded as idle, not counted twice.
- [ ] Reboot mid-session → session resumes (AUD-001), breaks already taken preserved.

## D. Offline & integrity
- [ ] Network off for 1 h while clocked in → rows queued, uploaded in order after reconnect, no duplicates in backend.
- [ ] Revoke device token → rows stay pending, upload after re-pairing.
- [ ] Re-enrol as a different employee on the same PC → previous employee's rows never uploaded under the new token (AUD-004).

## E. Environment
- [ ] Antivirus/EDR products in use: LL hooks and camera not blocked or flagged.
- [ ] Proxy, captive portal and metered networks.
- [ ] Multiple monitors, high DPI, dark/light theme for the tray UI.
- [ ] Camera absent / denied → face check flow degrades as designed.

## F. Pilot (T18)
- [ ] Pilot group completes a full workday; compare tray totals with backend reports.
- [ ] Support process and known-issue list (issue-register.md) accepted; go/no-go signed.
