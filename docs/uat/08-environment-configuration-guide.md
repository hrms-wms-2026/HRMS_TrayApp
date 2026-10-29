# 08 — Environment Configuration Guide (T13)

## Configuration sources (Service)

Generic host defaults, in increasing precedence: `appsettings.json` → `appsettings.{Environment}.json` → environment variables (`Agent__ApiBaseUrl`, …) → command line. The environment name comes from `DOTNET_ENVIRONMENT` (defaults to `Production`).

The tray has no configuration file; its only setting is the pipe name constant (`ONEVO.Agent.Pipe.v1`).

## `Agent` section

| Key | Default | Validation | Notes |
|---|---|---|---|
| `ApiBaseUrl` | `""` | Empty, or absolute **https** URL (enforced since `694d496`) | Empty → records stay buffered, nothing is uploaded |
| `IngestIntervalSeconds` | 150 | ≥ 30 | Upload cadence |
| `HttpTimeoutSeconds` | 30 | 5–300 | **Not applied** — no HttpClient registration yet (ISS-001/019) |
| `QueueCapacityMb` | 100 | 1–1000 | **Unused** — buffer is capped at 5,000 records (≈3.8 MB measured) |
| `HeartbeatIntervalSeconds` | 60 | ≥ 10 | Unused (no heartbeat endpoint) |
| `PolicyRefreshIntervalSeconds` | 3600 | none | Unused |
| `MaxReconnectAttempts` | 5 | none | Unused (tray uses `Constants.ReconnectMaxAttempts`) |
| `ForceMonitoringActive` | false | none | Declared "development only" but **not read anywhere**; must stay false |

## Environment separation

| Environment | `ApiBaseUrl` | Logging | Notes |
|---|---|---|---|
| Development | developer's https endpoint | `appsettings.Development.json`: Debug | — |
| UAT | **to be provided by the backend team** (no UAT endpoint is known to this audit) | Information | Set via installer-written `appsettings.json` or machine environment variable `Agent__ApiBaseUrl` |
| Production | — | Information | Not in scope |

## Secrets and certificates

- No secrets, tokens, test credentials or localhost URLs are committed in source or `appsettings*.json` (checked at `ef81a0c`). Test code uses only synthetic values (`test-device-token`, `*.test.invalid`).
- The device JWT is the only secret; it is created at enrollment (not implemented) and stored DPAPI-encrypted with a SYSTEM/Administrators-only ACL.
- TLS uses the Windows certificate store; no pinning. A UAT endpoint with a private CA requires that CA to be deployed to devices.

## Feature flags

None exist. Collector enablement is meant to come from `AgentPolicy` (`ActivitySignalEnabled`, `AppUsageEnabled`, `ScreenshotEnabled`, `CameraVerificationEnabled`); the default local policy enables activity counts only, and no policy is ever pushed to the tray yet (ISS-001).
