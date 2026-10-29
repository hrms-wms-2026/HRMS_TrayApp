# 11 — Remaining Manual Testing Checklist (T16 / T18 input)

Preconditions before any of this is meaningful: ISS-005 (buildable tray), ISS-001 (pipeline wired), ISS-002 (enrollment) and an installer (T12). Items marked **now** can be run today with the CI service artifact.

Use dedicated test devices and synthetic test accounts only. Never use production employee data.

## A. Service (runnable now)

- [ ] **now** Install service from `onevo-agent-service-1.0.0-ci.<run>` (see 07); it starts and stays running.
- [ ] **now** `ApiBaseUrl=http://…` → service refuses to start with a validation error in the event log.
- [ ] **now** Stop the service with an unreachable https `ApiBaseUrl` → stops within ~5 s.
- [ ] **now** After a credential write (requires enrollment, or a test harness), `icacls "%ProgramData%\ONEVO\Agent"` lists only SYSTEM and Administrators, inheritance disabled.
- [ ] **now** As a standard user, `type "%ProgramData%\ONEVO\Agent\credential.dat"` → access denied.
- [ ] **now** Pre-create `%ProgramData%\ONEVO\Agent` as a standard user, then trigger a credential write → folder owner becomes Administrators, user access removed.

## B. Tray ↔ Service (needs ISS-005)

- [ ] Tray started before the service → connects automatically once the service starts.
- [ ] Restart the service while the tray runs → tray reconnects without user action.
- [ ] Start a second tray instance in the same session → refused (needs ISS-016).
- [ ] Two users logged on (fast user switching) → behaviour documented (ISS-022).
- [ ] Exit the tray from the menu → no crash dialog / no WER report (ISS-035).

## C. Activity capture (needs ISS-001, ISS-012)

- [ ] Typing and clicking produce non-zero counts; no key codes/coordinates/window titles anywhere (inspect payloads with a test backend).
- [ ] No system-wide input lag with the tray running (ISS-012).
- [ ] 50 s idle then 10 s typing → snapshot ≈ idle 50 / active 10.
- [ ] Pause/break 10 s after a snapshot → final snapshot covers ≈10 s, not 60.
- [ ] Lock workstation 5 min → idle time, no active time.
- [ ] Sleep 30 min / hibernate → no snapshot reports more than one interval; no work time for the sleep period.
- [ ] Pull network for 1 h → records buffered, uploaded after reconnect in capture order, no duplicates in backend.
- [ ] Revoke device token → 401 logged, records kept, uploaded after re-enrollment.
- [ ] Reboot with network down → **expected data loss today** (ISS-004): record the amount.

## D. Attendance (needs ISS-003 — currently not implementable)

- [ ] Clock-in/out, break start/end, cross-midnight shifts, time-zone/DST changes, double-click protection.

## E. Environment & distribution (T16/T17)

- [ ] Fresh Windows 10 22H2 and Windows 11 devices, standard user, domain-joined and not.
- [ ] Antivirus/EDR products in use: LL hooks not blocked/flagged; signed binaries.
- [ ] Proxy / captive portal / metered network behaviour.
- [ ] Installer upgrade from previous build preserves enrollment; uninstall removes service and decides on `%ProgramData%` data.
