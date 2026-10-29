# 06 — Tray ↔ Service ↔ Backend API Integration Report (T04)

## 1. Backend endpoint matrix

Only one backend endpoint is referenced anywhere in the code.

| # | Method & route | Caller | Auth | Request DTO | Success | Error handling | Timeout / cancel | Status |
|---|---|---|---|---|---|---|---|---|
| 1 | `POST /api/v1/monitoring/activity/snapshots` | `ActivitySyncService.FlushAsync` | `Authorization: Bearer <device JWT>` | `ActivityIngestRequest` | 200 or 202 | 401/403 → drop (ISS-007); other → requeue; network exception → requeue | HttpClient default 100 s (no named client registered; `HttpTimeoutSeconds` unused); stop-token honoured except final flush (ISS-021) | **Not reachable at runtime** — service not registered (ISS-001) |

Endpoints implied by the product but **absent**: enrollment/login, token refresh, policy fetch, heartbeat (`HeartbeatIntervalSeconds` configured but unused), attendance clock-in/out, break start/end, de-enrollment.

### Request body (snake_case, `System.Text.Json`)

```json
{
  "snapshots": [
    {
      "captured_at": "2026-10-29T10:00:00+00:00",
      "keyboard_events_count": 42,
      "mouse_events_count": 17,
      "active_seconds": 55,
      "idle_seconds": 5,
      "intensity_score": 19.83,
      "foreground_process_name": "code.exe"
    }
  ]
}
```

Field constraints produced by the client: counts 0–100,000 (soft cap per interval); `active_seconds + idle_seconds` = interval length; `intensity_score` 0–100 with 2 decimals; process name matches `^[a-z0-9][a-z0-9._-]{0,98}\.exe$` or is null. Batch size ≤ 100.

### Compatibility gaps

| Gap | Impact | Issue |
|---|---|---|
| No `event_id` / idempotency key | Duplicate rows after lost responses | ISS-009 (backend change) |
| No device/user identifier besides JWT | Multi-user attribution | ISS-022 |
| Backend contract source not available | DTO compatibility cannot be verified against the server; schema comment in `ActivitySnapshotPayload.cs` is the only reference | Manual verification |

## 2. Tray ↔ Service IPC contract

Transport: named pipe `ONEVO.Agent.Pipe.v1`, byte mode, one JSON `IpcEnvelope` per line (`{"v":"1.0","type":...,"cid":...,"ts":...,"payload":...}`), max 64 KB per line, protocol version must equal `1.0`.

| Message | Direction | Implemented sender | Implemented handler | Notes |
|---|---|---|---|---|
| `NonceChallenge` | S → T | yes | yes | Echo handshake; no security value (ISS-011) |
| `NonceResponse` | T → S | yes | yes | |
| `StatusRequest` | T → S | yes | yes | Could be lost after handshake (ISS-006) |
| `StatusResponse` | S → T | yes | yes | Only on request (ISS-026) |
| `CollectionRecordSubmit` | T → S | yes | **no** — silently ignored | ISS-001 |
| `CollectionRecordAck` | S → T | **no** | ignored | No delivery guarantee tray → service |
| `PolicyPush` | S → T | **no** | yes | Collectors can never start (ISS-001) |

## 3. Automated contract tests

Added during this engagement (see 04/05):

- `IpcLineCodecTests` (Shared) — envelope parsing tolerant of BOM, size limit, malformed input.
- `NamedPipeAuthenticatorTests` (Service) — handshake followed by an immediately-sent message is not lost.
- `ActivitySyncServiceTests` (Service) — exact JSON wire format (snake_case field names, route, Bearer header) and status-code handling (200/202/401/403/5xx/network failure) against a stub `HttpMessageHandler`.

Not possible without a backend: live contract test against UAT. Recommended: a consumer-driven contract (e.g. a JSON schema published by the backend team) checked in CI.
