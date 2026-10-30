# 06 — Tray ↔ Service ↔ Backend Integration Report (T04)

## Backend endpoints used by the Service (`Api/AgentApiRoutes.cs`)

All calls go through the named `OnevoApi` HttpClient (base `Agent:ApiBaseUrl`, https only) with the device JWT. The exceptions are activation and device authorization, which create the token.

| Group | Endpoints |
|---|---|
| Activation / auth | `activation/exchange`, `activation/refresh`, `activation/revoke`, `activation/heartbeat`, `device-authorization/start`, `device-authorization/token` |
| Attendance | `tray/clock-in`, `tray/clock-out`, `tray/break/start`, `tray/break/end`, `tray/attendance-status`, `work-sessions` |
| Policy / tasks | `tray/policy`, `tray/daily-tasks` |
| Monitoring ingest | `activity/snapshots`, `app-usage/snapshots`, `device-state/snapshots`, `meetings/signals`, `tray/screenshots`, `tray/inactivity-attempts` |
| Check-in / face | `check-in`, `check-in/{id}/face-scan`, `check-in/face-preview`, `check-in/face-enroll`, `check-in/face-reference`, `biometrics/enrollment-attempts`, `…/{id}/complete` |
| Consent / legal | `permission-consent`, `/api/v1/legal/acceptances/complete-login` |
| Location | `location-change-requests` (submit, pending, respond), `tray/work-location/confirm` |
| Notifications | `tray/notifications/pending`, `…/{id}/ack`, `…/deliveries/pending`, `…/deliveries/{id}/ack` |
| Releases | `/api/v1/tray/releases/check` |

(Paths are under `/api/v1/monitoring/` unless shown in full.)

## Response handling (upload path, `ActivitySyncService`)

| Response | Behaviour |
|---|---|
| 2xx | Rows acknowledged. An optional body listing rejected items quarantines those items with the server's reason |
| 400 / 413 / 422 on a batch | Records re-sent one at a time; a record still rejected alone is quarantined (payload kept) |
| 401 / 403 | Rows stay pending; token refresh and policy sync resolve it |
| 5xx / 404 / 408 / 429 / timeout / network | Rows stay pending; exponential backoff with jitter, capped at 15 min (AUD-023 retries timeouts and 408) |
| 403 with a "capability disabled" problem code | Quarantined as `policy_disabled` |
| Capability disabled by policy | Rows quarantined as `policy_disabled` (requeue possible) |
| Older than 24 h | Quarantined before sending (`stale_over_24h`) |

Idempotency:
- Activity snapshots are de-duplicated server-side by capture time.
- Clock actions carry an idempotency header (`feature/checkin-idempotency-header`).
- Work sessions upsert by `SessionId`.
- Inactivity attempts are de-duplicated by attempt id.

## Tray ↔ Service IPC

- **Transport:** pipe `ONEVO.Agent.Pipe.v1`, one JSON `IpcEnvelope` per line, protocol version check, bounded line length, client verified before the handshake.
- **Message families** (~70 types in `IpcMessages.cs`):
  - status / policy push;
  - collection submit / ack (with `buffer_full` / `storage_error` codes);
  - lifecycle commands (clock-in/out, break);
  - enrollment and device pairing;
  - evidence transfer (chunked);
  - face / biometric enrollment;
  - location change, consent, legal acceptance;
  - notifications and daily tasks.

## Contract tests

- **Service side:** stub-handler tests exist for these areas:
  - sync outcomes;
  - token refresh;
  - notification polling;
  - clock-action recovery (AUD-009/012);
  - break race (AUD-035);
  - T15 in-process workday E2E with a backend outage.
- **Not available:** live tests against a UAT backend. Use the Postman collection in `docs/postman/` against UAT (manual checklist §D).

## Changed in this PR

None of the wire formats. UAT-01 changes only the *values* of `active_seconds` / `idle_seconds` in activity snapshots, which now match the covered time.
