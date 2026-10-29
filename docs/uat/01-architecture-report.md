# 01 — Technology Stack & Architecture Report (T01)

Baseline: `develop` @ `a05e7d9`, plus this branch's changes.

## Stack

| Layer | Technology |
|---|---|
| Language / SDK | C#, .NET SDK 10.0.300 (`global.json`, `rollForward: disable`) |
| Tray UI | .NET MAUI on WinUI (`net10.0-windows10.0.19041.0`), Windows head in `Platforms/Windows`, WinForms `NotifyIcon` for the tray icon, CommunityToolkit.Mvvm |
| Agent Service | .NET 10 Worker hosted as Windows Service **"ONEVO Agent Service"** (`UseWindowsService`) |
| Shared | `ONEVO.Agent.Shared` (`net10.0`): IPC messages, models, JSON file logger |
| Local storage | SQLite (`Microsoft.Data.Sqlite`) queue `agent_activity.db`; DPAPI-protected credential and evidence spool under `%ProgramData%\ONEVO\Agent` |
| IPC | Named pipe `ONEVO.Agent.Pipe.v1`, newline-delimited JSON envelopes, ~70 message types |
| Backend | HTTPS REST under `/api/v1/monitoring/*` (see 06), device JWT |
| Packaging | Signed MSIX (`build-msix.ps1`, `.github/workflows/release.yml`), Cloudflare R2 distribution |

## Components

```mermaid
flowchart LR
  subgraph User session
    UI[MAUI Tray UI<br/>onboarding, clock-in, session, face check]
    COL[Collectors<br/>ActivityCount, AppUsage, DeviceState,<br/>Meeting, InactivityScreenshot]
    COORD[CollectorCoordinator]
    PIPEC[NamedPipeClient]
    UI --> PIPEC
    COORD --> COL --> PIPEC
  end
  subgraph Session 0 — ONEVO Agent Service
    PIPES[NamedPipeServer<br/>+ IpcClientVerifier]
    WORKER[AgentWorker<br/>lifecycle, enrollment, clock actions]
    PRES[PresenceSession<br/>work/break/idle]
    BUF[(ActivityRecordBuffer<br/>SQLite)]
    SYNC[ActivitySyncService]
    BG[Heartbeat, TokenRefresh, PolicySync,<br/>NotificationPolling, AttendanceStatusSync,<br/>AgentCommandListener]
    PIPES --> WORKER --> PRES
    WORKER --> BUF --> SYNC
  end
  PIPEC <-->|named pipe| PIPES
  SYNC -->|HTTPS + device JWT| API[(ONEVO backend)]
  BG --> API
  WORKER --> API
```

Service hosted services (`Program.cs`): `AgentWorker`, `ActivitySyncService`, `HeartbeatService`, `TokenRefreshService`, `PolicySyncService`, `NotificationPollingService`, `AttendanceStatusSyncService`, `AgentCommandListener`.

## Responsibilities

- **Tray** (interactive session): UI, input-count hooks on a dedicated message-loop thread, collectors, camera/face capture, notifications. It never holds the device token.
- **Service** (LocalSystem):
  - credentials, enrollment, and the clock-in/break/clock-out calls to the backend;
  - attendance accounting (`PresenceSession`, the source of truth for work/break/idle);
  - the durable queue and upload, policy, and heartbeat.

## Lifecycle

| Event | Behaviour |
|---|---|
| Boot / logon | Service starts automatically (`scripts/install-service.ps1`: auto-start, restart on crash); tray starts at logon (MSIX) |
| Tray before Service | Tray shows Offline and blocks actions (AUD-005), retries in bursts then at a steady interval |
| Service restart | Open session rebuilt from the backend (AUD-001); pending queue rows survive in SQLite |
| Lock / user switch | Tray stops collectors (AUD-014 part 1); the gap counts as idle |
| Sleep / hibernate | Service receives SCM power events (AUD-014 part 2) |
| Second tray | One tray per session (AUD-006) |
| Logoff / shutdown | Bounded final flush (10 s); rows not sent stay pending |

## Data flow

Collectors → `CollectionRecordSubmit` (IPC) → `AgentWorker` → `ActivityRecordBuffer` (pending) → `ActivitySyncService` (peek → send → ack/quarantine/keep) → backend.

Attendance actions go Tray → IPC `LifecycleCommand` → Service → backend clock-in/break/clock-out endpoints, with `PresenceSession` tracking the local timeline.

## Storage

| Path | Contents | Protection |
|---|---|---|
| `%ProgramData%\ONEVO\Agent\` | `credential.dat` (DPAPI), `identity.json`, `EvidenceSpool\` (DPAPI), `logs\service.log` | Folder restricted to the Service accounts (AUD-003) |
| `%LocalAppData%\ONEVO\Agent\agent_activity.db` of the Service account (`systemprofile` when installed as a service) | Queue rows, session history | Profile of LocalSystem |
| `%LocalAppData%\ONEVO\Agent\logs\tray.log` of the employee | Tray log (rolling, Information+) | User profile |

## Dependencies

Centrally managed in `Directory.Packages.props` (.NET 10.0.14 servicing, MAUI 10.0.120 per AUD-022). No vulnerability gate in CI. Run `dotnet list package --vulnerable --include-transitive` before release.
