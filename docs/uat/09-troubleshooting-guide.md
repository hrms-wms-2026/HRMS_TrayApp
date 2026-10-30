# 09 — Troubleshooting Guide

Developer build/run issues (MAUI workload, `MSB3030` long path, locked DLLs) are covered in the repository `README.md`.

| Symptom | Likely cause | Check / fix |
|---|---|---|
| Tray shows **Offline**, actions blocked | Service not running or not installed (UAT-07) | `sc.exe query "ONEVO Agent Service"`; install with `scripts/install-service.ps1` |
| Tray not running after reboot | No autostart (UAT-08) | Start it from the Start menu; until it runs, elapsed time counts as idle |
| Service stops right after start | Configuration validation failed (non-https `ApiBaseUrl`, dev flag outside Development) | Read `service.log` / event log |
| Tray connects but Service refuses it | Tray not running from its installed location, or not in the console session (`IpcClientVerifier`) | Run the installed app; dev builds need `AllowIpcClientFromAnyPath` in Development |
| Clock-in returns an error | Backend unreachable, expired token, or lifecycle gate not met | `service.log` for the correlation id; token refresh retries automatically |
| Data not reaching the backend | Pending rows waiting on token / policy / backoff | `service.log` "stay pending" / "backing off" lines; rows survive restarts |
| Rows quarantined | Server refused content, stale (>24 h), policy disabled, or owner mismatch | `ONEVO.Agent.Service.exe --export-quarantine out.jsonl`; `--requeue-quarantine` after the cause is fixed |
| "Activity buffer database was unreadable" | Corrupt SQLite file | Original kept as `agent_activity.db.corrupt-<utc>`; a new queue was started |
| Second tray window will not open | Single-instance guard (AUD-006) | Use the existing tray icon |
| Input lag with tray running | Hooks blocked by security software (hooks run on their own thread) | Check EDR exclusions (manual checklist §E) |

## Logs

| Log | Location | Notes |
|---|---|---|
| Service | `%ProgramData%\ONEVO\Agent\logs\service.log` (rolling) + Windows event log | Correlation ids shared with tray and backend (AUD-018) |
| Tray | `%LocalAppData%\ONEVO\Agent\logs\tray.log` (rolling, 5 MB files) | Unhandled exceptions are captured too; contains per-snapshot activity counts (UAT-03) |
| Queue | `agent_activity.db` in the Service account's LocalAppData | Inspect with any SQLite tool (`collection_records.status`, `quarantine_reason`) |
