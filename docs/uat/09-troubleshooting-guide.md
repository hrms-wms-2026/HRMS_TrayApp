# 09 — Troubleshooting Guide

| Symptom | Likely cause | Check / fix |
|---|---|---|
| Service fails to start, event log shows `OptionsValidationException` | Invalid `Agent` settings, e.g. `ApiBaseUrl` is `http://` or relative | Use an absolute `https://` URL or leave empty (see 08) |
| Tray tooltip "Setup Required" | Service state is `Unenrolled` — expected today: enrollment is not implemented (ISS-002) | — |
| Tray tooltip stays "ONEVO WorkPulse — Starting..." | Tray not connected to the service | Is the service running (`sc.exe query "ONEVO Agent Service"`)? The tray retries indefinitely (backoff ≤30 s); after 5 failures it reports disconnected but keeps retrying |
| Tray shows "Ready" while the service is down | Disconnected state is displayed as `Stopped` (ISS-026) | Check the service |
| No activity reaches the backend | Expected today: pipeline not wired (ISS-001) | Once wired: credential present? `ApiBaseUrl` set? Logs "keeping N records" with the HTTP status |
| Logs show "Activity ingest rejected status=401/403 — keeping N records" | Device credential expired/revoked | Records are kept (not dropped) up to the 5,000-record buffer cap; re-enroll the device |
| Logs show "Corrupt activity record quarantined eventId=…" | A record could not be parsed | The record is dropped deliberately and logged by id; investigate the tray version |
| "IPC client dropped during handshake" (Information) | A process connected to the pipe and disconnected without completing the handshake | Harmless once; repeated occurrences may indicate probing (ISS-011) |
| "Pipe accept failed" (Error) repeating every second | Pipe could not be created (e.g. another process owns the pipe name) | Find the process holding `\\.\pipe\ONEVO.Agent.Pipe.v1` |
| Credential folder access denied for an administrator tool | Folder is intentionally limited to SYSTEM and Administrators | Run elevated |

## Where to find logs

- **Service:** Windows Event Log → Application, source = application name (`ONEVO.Agent.Service`). The generic-host EventLog provider records **Warning and above only** unless `Logging:EventLog:LogLevel` is configured, so Information messages (e.g. "Activity batch accepted") are not in the event log by default. Console output when run interactively.
- **Tray:** only the Debug output (`MauiProgram.cs` → `AddDebug`) — **nothing is persisted in release builds** (ISS-025). Use DebugView/attached debugger until file logging exists.

Logs never contain keystroke content, coordinates, window titles, counts, process names or tokens (by design; reviewed in 02).
