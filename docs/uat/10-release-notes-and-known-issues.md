# 10 — Release Notes & Known Issues

**Branch:** `claude/magical-curie-y2y0r4` · **Base:** `5cca93e` · **CI version stamp:** `1.0.0-ci.<run>`
**Status:** engineering hardening drop — **not a UAT release candidate** (see 12).

## Fixed

| Issue | Change |
|---|---|
| ISS-006 (P0) | IPC handshake no longer deadlocks on Windows and no longer drops the tray's first message: one BOM-less reader/writer per connection (`IpcLineCodec`). |
| ISS-007, 008 (P1) | Activity records are no longer lost on 401/403 or on unexpected errors during upload; corrupt records are quarantined with a log entry. |
| ISS-010, 024 (P1/P2) | Device credential folder restricted to SYSTEM/Administrators (squatted folders reclaimed); credential/identity files written atomically. |
| ISS-013 (P1) | Tray reconnects to the service indefinitely (boot ordering, service restart, upgrade). |
| ISS-014 (P1) | Active/idle seconds reflect the time actually covered; partial intervals (break/stop) no longer counted as a full minute; every idle span counted. |
| ISS-015 (P1) | Collector start/stop serialized — no capture loop left running during a break. |
| ISS-017 (P1) | Snapshot loop survives transient IPC errors. |
| ISS-018 (P2) | Pipe server: idempotent dispose, self-healing accept loop, logged handshake failures. |
| ISS-019 (P2) | `ApiBaseUrl` must be https. |
| ISS-021 (P2) | Service stop no longer waits on a hung backend (final flush ≤ 5 s). |
| ISS-030, 033 (P3) | Buffer keeps capture order on retry, atomic cap; HTTP responses disposed. |
| ISS-035 (P2) | Tray no longer risks a crash on exit from pipe-client disposal. |
| ISS-027, 028, 029 | `.gitignore` restored, build outputs untracked, Windows CI with hang guards, .NET 10.0.14 servicing packages. |

## Known issues (open)

| Issue | P | Summary | Owner decision needed |
|---|---|---|---|
| ISS-005 | P0 | TrayApp project produces no executable | Architecture: add MAUI Windows head or re-platform tray |
| ISS-001 | P0 | Activity pipeline not wired end to end | Product/engineering |
| ISS-002 | P0 | No login/enrollment/token lifecycle | Backend contract |
| ISS-003 | P0 | No clock-in/out or break tracking | Product scope |
| ISS-004 | P0 | Offline queue RAM-only; lost on restart/upgrade | Storage design (SQLite as planned) |
| ISS-009 | P1 | No idempotency key → duplicates possible | Backend contract |
| ISS-011 | P1 | IPC has no real client/server authentication | Security design |
| ISS-012 | P1 | Hooks installed without a message loop | Needs buildable tray + device testing |
| ISS-016 | P1 | No single-instance guard | Needs buildable tray |
| ISS-020, 022, 023, 025, 026 | P2 | Unbounded IPC line read; multi-user attribution; DeviceId = machine name; no persistent tray logs; misleading disconnected state | Various |
| ISS-031, 032, 034 | P3/Policy | State machine nits; tray UI init; employee can exit tray | Various |

## Compatibility notes

- IPC wire format unchanged (`v` = `1.0`); the service additionally accepts BOM-prefixed lines.
- `ActivityRecordBuffer.RequeueFront` now takes `IReadOnlyList<CollectionRecord>`; `ActivitySyncService` depends on `ICredentialStore` (both components are not yet registered in DI, so no runtime impact).
- No backend contract changes were made.
