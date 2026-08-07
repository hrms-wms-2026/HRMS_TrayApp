# Onexso WorkPulse — Tray App

Windows system-tray desktop agent for employee **clock-in / clock-out / break** and work-presence monitoring.

| Piece | Project | Role |
|-------|---------|------|
| **Tray UI** | `ONEVO.Agent.TrayApp` | MAUI Windows app — onboarding, clock-in, active session, face scan |
| **Agent Service** | `ONEVO.Agent.Service` | Background worker — Named Pipe IPC, lifecycle, collectors, API sync |
| **Shared** | `ONEVO.Agent.Shared` | IPC messages + models |

**Important:** Tray App alone is not enough for real clock-in. **Agent Service must be running** (Named Pipe). Backend API (`https://localhost:7229`) is optional for local UI demos; full activation/sync needs API.

---

## Prerequisites (Windows)

1. **.NET 10 SDK** (see `global.json`)
2. **.NET MAUI Windows workload**
   ```powershell
   dotnet workload install maui-windows
   ```
3. **Windows 10/11** (target `net10.0-windows10.0.19041.0`)
4. Open the **repo root** in VS Code: `C:\HR\tray_app_maui`

---

## Run from VS Code (easiest)

### One-time setup

1. Install **C# Dev Kit** (or **C#** + **.NET Install Tool**) extension.
2. `File → Open Folder…` → select `C:\HR\tray_app_maui`
3. Wait for restore (status bar).

This repo includes:

| File | Purpose |
|------|---------|
| `.vscode/launch.json` | F5 debug profiles |
| `.vscode/tasks.json` | Build + compound run tasks |

### Daily run (recommended order)

1. **Start Agent Service** (terminal or debug profile)
2. **Start Tray App**

#### Option A — Debug panel (F5)

1. Open **Run and Debug** (`Ctrl+Shift+D`)
2. Dropdown:
   - `Agent Service` → **Start Debugging** (or F5)
   - Then select `Tray App` → **Start Debugging** (F5)
3. Or use compound profile: **`Service + Tray App`** (starts both)

#### Option B — Terminal (VS Code integrated terminal)

```powershell
# Terminal 1 — Agent Service
cd C:\HR\tray_app_maui
dotnet run --project .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj -c Debug

# Terminal 2 — Tray App
dotnet run --project .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0
```

#### Option C — Already built EXE

```powershell
# After build
.\ONEVO.Agent.TrayApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ONEVO.Agent.TrayApp.exe
```

Service must already be running for clock-in IPC.

---

## Build only

```powershell
cd C:\HR\tray_app_maui

# Tray
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -c Debug

# Service
dotnet build .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj -c Debug

# All unit tests
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj
```

### Release publish (Tray)

```powershell
dotnet publish .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj `
  --configuration Release `
  -f net10.0-windows10.0.19041.0
```

---

## Employee UI flow (what you should see)

```
Connect (activation code)
  → Prepare workspace
  → Work location
  → Face verification
  → Confirm details
  → Allow policies
  → Clock In home          ← big CLOCK IN button
  → Active session         ← Working / On Break
  → Workday completed      ← after Clock Out
```

| Action | Expected |
|--------|----------|
| **CLOCK IN** | Goes to Active session (or face scan first if camera policy on) |
| **Break** | Status becomes On Break; break timer runs |
| **End Break** | Back to Working |
| **Clock Out** | Workday Completed summary |
| Window close (X) | Usually **hides to tray** (does not always kill process) |
| Tray icon double-click | Re-open window |

---

## Architecture (short)

```
[ Tray App UI ]
      │  Named Pipe (IPC)
      ▼
[ Agent Service ]  ──HTTP──►  [ Backend API https://localhost:7229 ]
```

- **No Device JWT in XAML/Preferences** for API calls — Service owns credentials.
- Collectors stop if IPC is lost (fail-safe).

Service config (API base URL):

- `ONEVO.Agent.Service/appsettings.Development.json` → `Agent:ApiBaseUrl`

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Tray opens but clock-in fails / “No response from service” | Start **Agent Service** first |
| Window not visible | Check **system tray** → Open Onexso |
| Build errors MAUI | `dotnet workload install maui-windows` then restore |
| Port / API issues | Backend is separate repo (`HRMS-Backend-v1`); tray UI still runs offline for mock onboarding |
| Second instance | Stop old process: `Stop-Process -Name ONEVO.Agent.TrayApp -Force` |
| VS Code F5 does nothing | Select correct profile in Run and Debug; open **folder root** not a single file |

### Stop processes

```powershell
Stop-Process -Name ONEVO.Agent.TrayApp -Force -ErrorAction SilentlyContinue
Stop-Process -Name ONEVO.Agent.Service -Force -ErrorAction SilentlyContinue
```

### Fix: DLL locked (`MSB3027` / file used by another process)

If you see:

```text
Could not copy ... ONEVO.Agent.Shared.dll ... locked by: "ONEVO.Agent.Service (xxxxx)"
```

**Cause:** Agent Service is **already running**. `dotnet run` / `dotnet build` cannot overwrite DLLs while that process holds them.

**Fix (copy-paste):**

```powershell
cd C:\HR\tray_app_maui

# 1) Kill old Service + Tray
Stop-Process -Name ONEVO.Agent.Service,ONEVO.Agent.TrayApp -Force -ErrorAction SilentlyContinue
# or:
# taskkill /F /IM ONEVO.Agent.Service.exe
# taskkill /F /IM ONEVO.Agent.TrayApp.exe

# 2) Confirm nothing left
Get-Process ONEVO.Agent.Service,ONEVO.Agent.TrayApp -ErrorAction SilentlyContinue

# 3) Then build / run again
dotnet run --project .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
```

**One-shot script (recommended):**

```powershell
cd C:\HR\tray_app_maui
powershell -ExecutionPolicy Bypass -File .\scripts\restart-agent.ps1
# Service + Tray together:
powershell -ExecutionPolicy Bypass -File .\scripts\restart-agent.ps1 -WithTray
```

**Rule:** Do not open a second `dotnet run` for Service while the first is still open. Close that window with `Ctrl+C`, or kill the process first.

---

## Project layout

```
tray_app_maui/
├── ONEVO.Agent.TrayApp/     # MAUI Windows tray UI
├── ONEVO.Agent.Service/     # Windows service / worker + Named Pipe
├── ONEVO.Agent.Shared/      # IPC + models
├── tests/                   # Unit tests
├── docs/                    # Plans, Postman, mockups
├── .vscode/                 # VS Code launch + tasks
└── README.md                # This file
```

---

## VS Code tips

1. **Compound launch** `Service + Tray App` = one F5 for both.
2. Use **two terminals** if you want separate log streams.
3. Set breakpoints in `ViewModels/*` or `Services/NamedPipeClient.cs`.
4. After XAML-only changes, rebuild Tray then re-run (hot reload may not always refresh desktop MAUI).

---

## Related docs

- Postman / API smoke: `docs/postman/README.md`
- Architecture checklist: use team skill / architecture doc if available
- UI design skill: `SKILL.md` (image → MAUI)

---

**Quick start (copy-paste)**

```powershell
cd C:\HR\tray_app_maui
# Terminal 1
dotnet run --project .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
# Terminal 2
dotnet run --project .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Or in VS Code: **Run and Debug → `Service + Tray App` → F5**.

## SQLite local database (durable save)

After Agent Service runs with this build, collection data is saved to:

```
%LocalAppData%\ONEVO\Agent\agent_activity.db
```

Typically: `C:\Users\<you>\AppData\Local\ONEVO\Agent\agent_activity.db`

| Table | What is stored |
|-------|----------------|
| `collection_records` | Keyboard/mouse activity, app usage, device state (JSON payload). `status` = `pending` or `synced` |
| `session_history` | Each completed day: clock in/out, work seconds, break seconds, break count |

- **Pending** rows wait for Device JWT + backend flush.
- Without JWT, data **still stays in SQLite** (not lost on restart).
- Inspect with any SQLite tool: `sqlite3 agent_activity.db "SELECT COUNT(*) FROM collection_records;"`

