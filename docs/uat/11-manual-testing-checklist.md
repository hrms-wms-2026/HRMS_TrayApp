# 11 — Manual Testing Checklist (T16 → T18)

Use dedicated test devices and synthetic test accounts only. Never use production employee data.

## A. Install & lifecycle
- [ ] Fresh Windows 10 22H2 and Windows 11, standard user: Service installed (07), MSIX installs, Service starts automatically after reboot.
- [ ] Sign in after a reboot → record whether the tray starts by itself (expected **no** until UAT-08 is fixed) and how the time before it is opened is classified.
- [ ] Start a second tray instance in the same session → only one stays running.
- [ ] Restart the Service while the tray runs → tray shows Offline, then reconnects without user action.
- [ ] Kill the tray from Task Manager while clocked in → reopen → session state restored from the Service.
- [ ] Upgrade from the previous build → enrollment kept, pending queue rows still uploaded.
- [ ] Upgrade while clocked in and while offline → session and queue survive; Service and tray versions match afterwards.
- [ ] Uninstall → Service stopped and removed, autostart entry removed, decision on `%ProgramData%\ONEVO\Agent` (keep/delete) applied (AUD-029).
- [ ] Install as a standard user without admin rights → record what is refused.

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

## F. Security on a device
- [ ] As a standard user, `type %ProgramData%\ONEVO\Agent\credential.dat` → access denied (UAT-09).
- [ ] Copy `ONEVO.Agent.TrayApp.exe` to Downloads and run it → the Service refuses the connection.
- [ ] Second Windows user signed in (fast user switching) → their tray is refused while not the console session.
- [ ] `tray.log` content reviewed against the monitoring policy (UAT-03).

## G. Resources (T10 phase B)
- [ ] `scripts/check-footprint.ps1` idle run passes on the UAT build.
- [ ] Full workday with all collectors enabled: record Service/Tray private bytes and CPU, then propose active budgets.

## H. Backend contract on the UAT backend (AUD-007)
- [ ] Every route in `AgentApiRoutes.cs` answers on the UAT backend with the expected verb (no 404/405); record `tray/daily-tasks` explicitly (UAT-15).
- [ ] Lose the response of an app-usage upload (drop network after send) → count backend rows; duplicates expected until UAT-16 is fixed.

## I. Privacy gates (AUD-010, UAT-18) — after D-05 is decided and implemented
- [ ] New employee who has not given consent → no collector starts, nothing queued.
- [ ] Employee on approved leave → clock-in and monitoring refused as decided.
- [ ] Policy disables activity → no activity rows are queued (not only quarantined).
- [ ] Fresh enrolment before the first policy arrives → no capture.

## J. Pilot (T18)
- [ ] Pilot group completes a full workday; compare tray totals with backend reports.
- [ ] Support process and known-issue list (issue-register.md) accepted; go/no-go signed.
