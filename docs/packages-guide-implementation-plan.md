# packages-guide.md → Implementation Plan (Tray + Service)

Source: `packages-guide.md` Part 2 (Desktop Agent) + Week 3 checklist.  
Scope: **this repo** (`tray_app_maui`) only — not HRMS backend Hangfire/MediatR.

## Status matrix

| Guide § | Package | Verdict | Status in repo |
|---------|---------|---------|----------------|
| 7 | Hosting.WindowsServices | Required | **Done** (`UseWindowsService`) |
| 8 | Microsoft.Data.Sqlite | Required local buffer | **Done** + 100MB size guard |
| 9 | Microsoft.Extensions.Http.Resilience | HTTP retry/circuit | **Done** (`OnevoApi` pipeline) |
| 10 | CommunityToolkit.Maui | Optional tray/toasts | **Skip** — WinForms `NotifyIcon` |
| 11 | Named Pipes | Required IPC | **Done** |
| 12 | Win32 P/Invoke | Activity/app tracking | **Done** (collectors) |
| — | SignalR.Client | Remote commands | **Done** skeleton (`AgentCommandListener`) |
| — | Serilog | Structured logs | Defer (ILogger works) |
| 13 | MSIX | Distribution | Defer (packaging project) |
| Part 1 | Hangfire/MediatR/JWT API | Backend | Out of scope (HRMS-Backend-v1) |

## Implemented this pass

1. **Http Resilience** on `OnevoApi` — retry 4× exponential + 429 Retry-After + circuit breaker + per-attempt timeout.
2. **SQLite size guard** — drop oldest pending when file &gt; 100MB + VACUUM.
3. **AgentCommandListener** — SignalR client; starts only with ApiBaseUrl + Device JWT; logs remote commands.
4. Plan doc + packages-guide alignment.

## Acceptance

- [x] Service builds with new packages  
- [x] DB path `%LocalAppData%\ONEVO\Agent\agent_activity.db`  
- [x] `dotnet test` Service 40 + Tray 102 green
