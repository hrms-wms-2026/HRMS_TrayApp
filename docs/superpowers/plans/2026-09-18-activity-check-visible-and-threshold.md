# Activity Check Visible + Threshold Mapping

> **For agentic workers:** Inline execution in this session. TDD for new behavior.

**Goal:** Activity check threshold from Monitoring settings drives the toast; the Allow/Skip prompt stays visible; Allow captures the full virtual desktop and lands on Daily Summary.

**Architecture:** Evidence from `tray-boot.log` (2026-09-18): collector starts when clocked in, `Show()` succeeds, then `timed_out` (~108s) because the employee never clicked. Idle buckets are ~120s while the Monitoring UI shows 1 minute. PolicySync only refreshes every 45 minutes. Capture-on-Allow and Daily Summary gallery already exist.

**Tech Stack:** .NET MAUI Windows, App SDK toasts, Named Pipe policy push.

## Evidence

| Layer | Status |
|---|---|
| Monitoring UI threshold field | Saved as `idleThresholdMinutes` (1–1440) |
| Backend `GetEffectiveTrayPolicy` | Maps Activity + Screenshot → `InactivityScreenshotEnabled`; includes `IdleThresholdMinutes` |
| Service `PolicySyncService` | Fetches once, then every **45 minutes** |
| Tray collector | Starts on Active; buckets = `IdleThresholdMinutes * 60` |
| Toast | `Show()` OK, default banner ~5s, then timeout |
| Allow capture | Virtual desktop JPEG already implemented; 09:33 Allow was `outcome=captured` |
| Daily Summary gallery | Wired this session (in-memory after Allow) |

## Tasks

1. In-app Activity check overlay on ActiveSession (GlassConfirmHost Allow/Skip) + Reminder toast.
2. Log `IdleThresholdMinutes` on policy receive / collector start.
3. Policy refresh every 2 minutes so a saved 1-minute threshold reaches the tray.
4. Tests for overlay routing and 2-minute refresh bound.
5. Restart Service + Tray.
