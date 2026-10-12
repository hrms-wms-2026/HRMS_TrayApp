# AGT-01 / AGT-02 / AGT-03 / SEC-01 remediation plan

Date: 2026-10-12. Source: `issues.pdf` (audit triage dated 2026-10-11), re-verified against the
live repos on this machine. The PDF is evidence only; every claim below was re-checked.

Repos in scope:

| Repo | Path | Role |
|---|---|---|
| Tray / agent | `C:\HR2\HRMS_TrayApp` (`hrms-wms-2026/HRMS_TrayApp`) | capture, SQLite buffer, upload |
| Backend | `C:\HR2\HRMS-Backend-v1` | `/api/v1/monitoring/*` ingest, tray JWT issue + validation |

Other clones found: `C:\HR\tray_app_maui` (same remote, branch `feature/tray-original-workday-screens`,
has pre-existing uncommitted `ConnectWorkspaceViewModel.cs` + untracked logs/jpg — not touched), `C:\wtt`
(worktree). The PDF path `C:\Users\User\OneDrive\Desktop\onexso\tray app\HRMS_TrayApp` does not exist here.
No AGENTS.md / CLAUDE.md in either repo.

## Verified data flow

```
TrayApp collector ──IPC CollectionRecordSubmit──▶ AgentWorker.HandleCollectionSubmitAsync
  ──▶ ActivityRecordBuffer.TryEnqueue (SQLite, status=pending)   [ack AcceptedCount → tray ignores it]
ActivitySyncService (every IngestIntervalSeconds=150s, 100 rows/flush)
  ──▶ PeekPendingBatch ─▶ policy filter ─▶ stale (>24h) quarantine (PR #54)
  ──▶ POST /api/v1/monitoring/{activity,app-usage,device-state}/snapshots, /meetings/signals
Backend handler: Device JWT → tenant/user/device; toggle check (403); SnapshotTimeWindow
  (PR #175, OPEN: per-item window, all-out-of-window → 400); dedupe (activity only); SaveChanges → 202
Client: 2xx → MarkAcknowledged (status=synced; rows never deleted)
```

## AGT-01 — offline batch data loss

**Verified root cause** (`ActivitySyncService.PostBatchAsync`, `PostFormAsync`):
- 400 → returns `[]` → whole batch marked `synced` → **silently lost**. PR #54 only pre-quarantines >24h rows by
  client clock; any other 400 (validation, clock skew at the edge) still drops the batch.
- 401/403 → returns `[]` → whole batch marked `synced` (“dropping pending re-enrollment”) → **lost** on an
  expired/rotated token.
- Corrupt JSON row: logged “quarantined” but never quarantined; it is implicitly acknowledged.
- `QuarantineRow(reason)` **overwrites `payload_json`** with the reason → quarantined data is destroyed.
- Backend before PR #175: one item >24h → whole batch 400. PR #175 fixes this server-side but is **OPEN**.
- Idempotency: activity dedupes on `(tenant, device, captured_at)` (unique index). App usage, device state and
  meeting signals have **no dedupe** → a lost 202 response + retry inserts duplicates.
- 24h window: documented design rule (`2026-08-05-activity-monitoring-keyboard-mouse-tracking.md`
  “CapturedAt … not older than 24h”). Treated as intentional → preserved; rejected rows kept visibly.

**Planned behavior**
1. Quarantine keeps the original payload; reason + timestamp go in new nullable columns
   (`quarantine_reason`, `quarantined_at`, added by the existing `EnsureColumnUnlocked` migration path).
2. Batch outcome is per record:
   - 2xx → ack. If the (new, optional) response body lists rejected items, those are quarantined with the
     server reason instead of acked. Old servers send no body → unchanged behavior.
   - 400/413/422 on a multi-record batch → isolate: resend records one at a time (bounded by batch size);
     a record that is still rejected alone is quarantined with `rejected_<status>`; the rest are acked.
   - 401/403 → **leave pending** (token refresh / policy sync resolve it), stop this flush.
   - 5xx / timeout / network → leave pending; consecutive-failure exponential backoff with jitter,
     capped at 15 min.
   - Corrupt payload → real quarantine (`corrupt_payload`).
3. Backend (on top of PR #175): idempotent replay for app usage / device state / meeting signals using their
   natural keys (same approach activity already uses); 202 body reports `accepted / duplicates / rejected[]`
   (additive, old agents ignore it). Tenant/employee ownership checks untouched (Device JWT only).
4. Recovery/export: `ONEVO.Agent.Service.exe --export-quarantine <file>` writes quarantined rows as JSONL.

**Compatibility**: new agent + old backend (no body) → acks as today, stale pre-quarantine still applies.
Old agent + new backend → body ignored; mixed batches now partially stored (PR #175).

## AGT-02 — buffer limit data loss

**Verified root cause** (`ActivityRecordBuffer`):
- Cap = `QueueMaxRecords` 5,000 pending rows (default). At the cap `TryEnqueue` returns false → new record
  dropped; tray ignores the ack, so nothing visible. 5,000 rows ≈ 8.7 days at one row per 150 s.
- Byte cap hardcoded 100 MB (`QueueCapacityMb` validated but never used). Over it, **every enqueue deletes the
  1,000 oldest pending rows of any type (incl. work sessions)** + VACUUM, silently (`catch {}`).
- Synced rows are never deleted → file only grows → byte cap eventually eats unsent data.
- Stale rows stay `pending` while offline, so dead data fills the cap and blocks fresh data.

**Planned behavior** (no pending row is ever evicted):
- Low-priority snapshots may fill `QueueMaxRecords`; lifecycle/evidence records (work session, inactivity
  attempt, face photo) get a reserved headroom above it (hard limit = cap + reserve).
- At the cap: quarantine stale snapshots (kept, not deleted), then reject the new low-priority record.
- Byte pressure: purge already-`synced` rows only (except synced inactivity attempts, used for dedupe).
  Pending **and quarantined** rows are never deleted automatically; if they alone fill the hard limit the
  buffer reports `Full` and new low-priority capture is refused visibly. Recovery is explicit
  (`--export-quarantine` / `--requeue-quarantine`). A quarantine retention period is an open product decision.
- Screenshots do **not** get the reserved headroom (they arrive on a schedule and would consume it).
- Health: `Ok / Warning (≥80%) / Full / StorageError`, logged on transition, exposed on `StatusResponse`
  (additive IPC field) and in the `CollectionRecordAck.ErrorCode` (`buffer_full`, `storage_error`).
- SQLite write failure / disk full → `TryEnqueue` returns false with `StorageError`, nothing deleted.
- Corrupt DB at open → file renamed `*.corrupt-<utc>` (preserved), fresh DB created, error logged.
- Drain: up to N batches per tick while the backend is accepting; restart-safe (rows pending until ack).

## SEC-01 — tray JWT secret

**Verified**: the secret is a **backend** HS256 signing/validation key (`TrayTokenService` signs,
`AuthenticationExtensions` "TrayDeviceScheme" validates). The tray only receives tokens; tray appsettings has
no secret (verified). Tracked value in `src/ONEVO.Api/appsettings.Development.json` is a self-labeled dev
placeholder (also copied in tracked `.codex-build/`); base `appsettings.json` has no secret, so missing
config already throws. No real credential found in tracked files.

**Planned behavior**: one validator used at startup and by both consumers: required, ≥32 UTF-8 bytes (HS256
key size), and outside Development/Test reject `dev-only…`/placeholder values. Error messages never contain
the value. `.env.example` + deployment doc list `TrayApp__Jwt__Secret` with placeholders only.
Production config is **not** verifiable from here (no access) — documented, not verified.

## AGT-03 — git / release traceability

**Verified** (2026-10-12): no clone on this machine has uncommitted `AgentWorker.cs`/`OnevoApiClient.cs`/
`IpcMessages.cs`; local `main` == `origin/main` in both clones. The real gap: GitHub default branch `main`
is still `5cca93e first commit` (2026-08-05); `origin/develop` is **226 commits ahead** of it. The 9-file /
213-commit state lives only on the reporter's machine and cannot be verified or published from here.

**Plan**: git bundle snapshot of both local repos before any integration; secret scan of
`origin/main..origin/develop` + fix branches; fixes on topic branches off `origin/develop`; release =
PR `develop → main` after fixes merge; fresh clone of the exact release commit builds + tests.
Publication (push / PR / merge) waits for explicit approval.

## Order & validation
1. Snapshot git state → 2. AGT-01 → 3. AGT-02 → 4. SEC-01 → 5. AGT-03 prep.
Validation: unit tests per scenario, full Service/Backend unit suites, offline→reconnect smoke test against a
local stub server, fresh-clone build/test of the release candidate commit.
