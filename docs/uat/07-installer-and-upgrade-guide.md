# 07 — Installer & Upgrade Guide (T12)

## What exists

| Artifact | Built by | Contains |
|---|---|---|
| Signed **MSIX** (`com.onevo.workpulse.agent`) | `build-msix.ps1` (local) / `.github/workflows/release.yml` (tag `vX.Y.Z`) | **Tray app only**, plus the `onexso-workspace:` protocol handler |
| Agent Service install | `scripts/install-service.ps1` (elevated, from a repo checkout with the SDK) | Publishes `ONEVO.Agent.Service` to `%ProgramFiles%\ONEVO\AgentService`, registers it with auto-start and restart on crash |
| Release registration | `release.yml` | Uploads the MSIX to R2 and registers an **inactive beta** in the platform; an admin activates and promotes it |

## Gaps (see 03)

- **UAT-07 (P1):** the released MSIX does **not** include or install the Agent Service, and nothing outside the developer script installs it. A device that only gets the MSIX has a tray that shows Offline forever.
- **UAT-08 (P1):** the manifest has no `windows.startupTask`, and no Run key or scheduled task exists. The tray does **not** start at logon. After a reboot mid-day the Service sees no tray IPC, and its gap fallback counts the time as idle until the employee opens the tray.

Both need a packaging decision before UAT distribution. Options include:
- an MSIX `desktop6:Service` extension plus `windows.startupTask`; or
- an MSI/bootstrapper that installs the Service and registers the tray startup.

## UAT device install today (developer procedure)

1. Install the .NET 10 Desktop Runtime.
2. Elevated PowerShell in a repo checkout at a short path (≤103 characters, AUD-021), with the SDK:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1
   ```

3. Trust the signing certificate (dev cert: import into `LocalMachine\TrustedPeople`), then install the MSIX.
4. Set `Agent:ApiBaseUrl` for the UAT backend in the installed Service's `appsettings.json` (https only), then restart the Service.

Uninstall: `scripts\uninstall-service.ps1` (elevated), then remove the app from Settings → Apps.

## Upgrade

| Item | Behaviour |
|---|---|
| Tray | New MSIX version via Tray Releases (beta → stable); a minimum supported version can force upgrades |
| Service | Re-run `install-service.ps1`; it stops and replaces the prior install |
| Queue / enrollment | SQLite queue and `%ProgramData%\ONEVO\Agent` files survive; pending rows upload after restart; a corrupt DB is preserved as `*.corrupt-<utc>` |
| IPC compatibility | Tray and Service must be upgraded together (protocol version check) |
| Rollback | Deactivate the bad release in Platform Admin; the previous stable becomes latest |

## Quarantine tools (Service)

`ONEVO.Agent.Service.exe --export-quarantine <file>` writes quarantined rows as JSONL. `--requeue-quarantine` returns them to pending. Recovery is explicit (AGT-02).
