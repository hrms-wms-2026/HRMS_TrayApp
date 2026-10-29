# 07 — Installer & Upgrade Guide (T12)

## Status: no installer exists

There is no installer project (WiX/MSIX/Inno), no service registration script, no tray autostart mechanism and no uninstall routine in the repository (ISS-028). The TrayApp produces no executable (ISS-005). An installer therefore **cannot be built or validated**, and none is delivered.

What CI does produce: `onevo-agent-service-1.0.0-ci.<run>` — a framework-dependent `win-x64` publish of the Windows Service (requires the .NET 10 runtime on the device).

## Requirements an installer must meet (derived from the code)

| Requirement | Source |
|---|---|
| Install .NET 10 Desktop + ASP.NET-free runtime, or publish self-contained | `net10.0-windows` targets |
| Register service `ONEVO Agent Service` (LocalSystem, automatic delayed start) | `Program.cs` `UseWindowsService` |
| Service must start **before** trays connect, or trays must retry (now they do — ISS-013) | `NamedPipeClient` |
| Start the tray in every interactive user session at logon (Run key or scheduled task), one instance per session | ISS-016 |
| Create `%ProgramData%\ONEVO\Agent` **as SYSTEM** — the service now applies a protected ACL itself on first write (ISS-010) | `SecureAgentStorage` |
| Write `appsettings.json` with the environment's `Agent:ApiBaseUrl` (https only) | `OptionsValidation` |
| Sign all binaries and the installer (T17) | — |

## Upgrade & event preservation

| Concern | Current behaviour | Risk |
|---|---|---|
| Buffered activity during upgrade | Buffer is RAM-only; stopping the service discards unsent records (after a ≤5 s final flush attempt) | **Data loss on every upgrade** until ISS-004 is fixed |
| Credential/identity files | Kept in `%ProgramData%\ONEVO\Agent`; written atomically | Uninstall must decide whether to delete them (device de-enrollment) |
| IPC compatibility | Server tolerates BOM-prefixed lines; protocol version must equal `1.0` | Tray and service must be upgraded together |
| Rollback | No mechanism | — |

## Manual install procedure for developer/UAT smoke tests (service only)

Run in an elevated PowerShell on a test machine, using CI artifact `onevo-agent-service-1.0.0-ci.<run>` extracted to `C:\Program Files\ONEVO\Agent`:

```powershell
sc.exe create "ONEVO Agent Service" binPath= "\"C:\Program Files\ONEVO\Agent\ONEVO.Agent.Service.exe\"" start= delayed-auto
```

```powershell
sc.exe start "ONEVO Agent Service"
```

Uninstall:

```powershell
sc.exe stop "ONEVO Agent Service"
```

```powershell
sc.exe delete "ONEVO Agent Service"
```

Without an enrolled credential and a buildable tray the service only answers status requests (`Unenrolled`); this procedure verifies service start/stop and IPC only.
