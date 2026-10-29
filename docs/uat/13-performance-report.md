# 13 — CPU, RAM, Disk & Network Report (T10)

Scope limit: the tray cannot be built (ISS-005) and the pipeline is not wired (ISS-001), so whole-process CPU/RAM on a device could **not** be measured. What follows is measured on component level (Linux, Release build, .NET 10.0.x) plus a static inventory of all periodic work.

## Measured

| Measurement | Result | Method |
|---|---|---|
| Service buffer memory at cap (5,000 records) | **≈3.8 MB** retained (≈757 B/record) | `GC.GetTotalMemory` before/after filling `ActivityRecordBuffer` with realistic snapshots |
| Upload body, 100 snapshots | **≈21.5 KB** (≈215 B/snapshot) | Serialized `ActivityIngestRequest` |
| Buffer dequeue + requeue of 100 records | ≈12 µs | Stopwatch, 200 iterations |

Implication: at the default 1 snapshot/min the service uploads ~2–3 snapshots every 150 s (≈0.5 KB per request, ≈300 KB/day); a full buffer after ~3.5 days offline drains in 50 requests of ≈21.5 KB, one per 150 s tick (≈2 h). `QueueCapacityMb` (100) is unused; the effective bound is the 5,000-record cap (ISS-030).

## Periodic work inventory (after fixes)

| Component | Period | Work per tick | Notes |
|---|---|---|---|
| Tray activity sampling | 5 s | 1× `GetLastInputInfo`, arithmetic | Added for ISS-014 (previously one sample per 60 s); negligible CPU |
| Tray snapshot | 60 s | 1 record over IPC, `GetForegroundWindow` + process lookup | |
| LL keyboard/mouse hooks | per input event | lock + increment | Must run on a message-loop thread (ISS-012) or input lags system-wide |
| Tray reconnect | 1 s → 30 s backoff while disconnected | pipe connect attempt | Capped; no busy loop |
| Service sync | 150 s (`IngestIntervalSeconds`) | ≤100 records per request | One batch per tick |
| Service accept loop | event-driven | — | 1 s retry delay only after a pipe-creation failure |

## Disk

The service writes only `credential.dat` and `identity.json` (both < 2 KB) and nothing else; there is no log file (event log only) and no queue file (ISS-004). No unbounded disk growth is possible today; a durable queue (ISS-004) will need an explicit size cap.

## Leaks / resource findings

| Finding | Status |
|---|---|
| `HttpResponseMessage` never disposed | Fixed (ISS-033) |
| Pipe server instances left after failed accept | Fixed (ISS-018) |
| Orphaned snapshot loop after start/stop race | Fixed (ISS-015) |
| Unbounded IPC line buffering (memory DoS by a local process) | Open (ISS-020) |

## Still to measure on a device (with a buildable tray)

Tray working set and CPU over an 8-hour workday, input latency with hooks installed, service working set with a full buffer, network usage on a metered connection.
