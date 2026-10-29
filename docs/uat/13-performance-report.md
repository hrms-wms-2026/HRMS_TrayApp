# 13 — CPU, RAM, Disk & Network Report (T10)

## Idle budgets (T10 phase A, already on `develop`)

`footprint-budgets.json`, measured 2026-10-28 on `develop` 4fabeec (Release, connected and unenrolled, two runs):

| Process | Measured | Budget |
|---|---|---|
| Service | 71–73 MB working set / 21–23 MB private | 80 MB / 30 MB |
| Tray | 188–198 MB private; working set 24–286 MB (Windows trims it when the window is in the background) | 320 MB / 230 MB |
| Total CPU | < 0.1% | 2.0% average |

`scripts/check-footprint.ps1` samples both processes for 5 minutes and fails if anything is over budget. Run it manually before a release; it is not part of CI.

## Active-monitoring budgets

Not yet set (T10 phase B). They need a measured run on a device with an enrolled employee and all collectors enabled; see the manual checklist §G.

## Periodic work (active monitoring)

| Component | Period | Note |
|---|---|---|
| Activity snapshot sampling | 5 s (this PR) | One `GetLastInputInfo` call per sample; negligible CPU |
| Activity snapshot emit | 60 s | One IPC record |
| Device state | 60 s | GPS fix only every 15th tick, if location tracking is enabled |
| Upload | 150 s (`IngestIntervalSeconds`) | ≤ `MaxBatchesPerFlush` batches while the backend accepts; backoff ≤ 15 min |
| Heartbeat | 30 s | |
| Policy | 3600 s | |
| LL hooks | Per input event | Dedicated thread; lock + increment only |

## Disk

| Store | Bound |
|---|---|
| SQLite queue | `QueueMaxRecords` 5,000 pending + 500 reserve; database limit `QueueCapacityMb` 100 MB; synced rows purged under pressure (AGT-02) |
| Evidence spool | Deleted after delivery; expiry per attempt |
| `tray.log` / `service.log` | Rolling 5 MB files with fixed retention |
