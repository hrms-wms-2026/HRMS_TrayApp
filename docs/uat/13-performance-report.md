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

## Installer (MSIX) size

Measured with `build-msix.ps1` (Release, win-x64, throwaway self-signed certificate) on `windows-latest`:

| | `develop` @ `ae33883` | After PR #101 |
|---|---|---|
| `.msix` download | **48.25 MB** | **43.85 MB** |
| Installed (unpacked) | 114.9 MB, 407 files | 110.5 MB, 294 files |
| PNG images (unpacked) | 12.3 MB, 264 files | 7.95 MB, 151 files |

The Windows App Runtime 1.8 (x64 41 MB) is a separate dependency package. It is installed once per PC if it is missing.

Where the rest goes (unpacked):

| Item | Size | Note |
|---|---|---|
| `Microsoft.Windows.SDK.NET.dll` + `Microsoft.WinUI.dll` | 39.4 MB | Needed by the app |
| QuestPDF (qpdf, Skia, Lato fonts, libstdc++, QuestPDF.dll) | ≈ 29 MB | Only the daily-summary PDF uses it. Moving PDF generation elsewhere is a product decision |
| MAUI and other DLLs | rest | |

PR #101 removed only images that no screen uses and re-encoded the others losslessly (identical pixels). `ImageResourceContractTests` keeps both rules. Not done, because each would change the UI or UX or needs a decision:
- lossy colour reduction of the hero images (about −5 MB more);
- loading images from the backend;
- removing QuestPDF.

## Storage — measurements and compression options

Measured on Linux with `develop`'s real `ActivityRecordBuffer` (SQLite file) and a synthetic 8-hour day:
- 480 activity, 480 app-usage and 480 device-state snapshots;
- 240 meeting signals;
- one face photo.

| Measure | 100 KB photo | 200 KB photo | 400 KB photo |
|---|---|---|---|
| Rows per day | 1,681 | 1,681 | 1,681 |
| DB growth per day (pending) | 0.75 MB | 0.89 MB | 1.15 MB |
| DB size after upload (rows kept as `synced`) | 0.91 MB | 1.20 MB | 1.73 MB |
| Days until the 100 MB limit (purge + full VACUUM) | ≈110 | ≈83 | ≈58 |
| After deleting synced rows + VACUUM | 32 KB | 32 KB | 32 KB |

Where the bytes go (200 KB photo):

| Record type | Payload | Other columns |
|---|---|---|
| Face photo (1 row) | 281 KB (base64 of a 195 KB JPEG) | 0.1 KB |
| Activity snapshots | 90 KB | 62 KB |
| Device state | 64 KB | 64 KB |
| App usage | 61 KB | 62 KB |
| Meeting signals | 24 KB | 30 KB |

Compression of the small JSON payloads (239 KB/day raw):

| Method | Size | Verdict |
|---|---|---|
| Brotli per row (what compressing `payload_json` would give) | 84% of raw | **Not worth it**: rows are too small to compress |
| gzip of a whole upload batch | 9% of raw | Large saving on the **network**. Needs the backend to accept `Content-Encoding: gzip`, so it is a contract change |

### Result of option 1 + 3 (implemented in PR #100, `a8dab05`)

30 simulated days (same synthetic day, uploaded daily), with the real buffer:

| | Day 1 | Day 7 | Day 30 | Later |
|---|---|---|---|---|
| `develop` before | 1.19 MB | 6.49 MB | 26.9 MB | 100 MB around day 83, then purge + full VACUUM |
| With UAT-13 fix | ≈1.2 MB | ≈1.2 MB | ≈1.2 MB | stays ≈1.2–1.7 MB (the last 24 h of delivered rows) plus any pending backlog |

Purge cost: 339 ms for 30 purges (~11 ms each).

### What to do (in order of value)

1. **Done in PR #100 — delete synced rows soon after acknowledgement (UAT-13).** Keep a short de-duplication window. Steady-state storage drops from tens of MB to well under 1 MB per employee, and delivered GPS and activity data stop lingering on the device.
2. **Stop storing face photos as base64 in SQLite (UAT-12).** Spool the JPEG bytes DPAPI-encrypted, as screenshots already are, and delete them after delivery. This is −25% size plus encryption, and the largest single item per day.
3. **Done in PR #100 — incremental auto-vacuum instead of a full `VACUUM` at 100 MB.** No long lock, and no 2× temporary disk.
4. **Request compression for uploads (gzip).** About −90% of upload bytes for snapshot batches. Needs backend support first.
5. **Screenshot byte budget.** `Constants.MaxScreenshotBytes` allows ≈9.5 MB per screenshot against a 256 MB / 72 h spool, so roughly 25 maximum-size screenshots fill it. A lower budget saves disk and network but lowers evidence quality, which is a product decision.
6. **Not recommended:** per-row compression of `payload_json`, which saves only 16% for extra CPU and opaque rows.

Logs are already bounded: `tray.log` and `service.log` are each 5 MB × 3 files.
