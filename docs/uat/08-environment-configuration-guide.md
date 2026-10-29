# 08 — Environment Configuration Guide (T13)

## Sources (Service)

Generic host precedence: `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` → environment variables (`Agent__ApiBaseUrl`, …) → command line. `DOTNET_ENVIRONMENT` defaults to `Production`. Options are validated at start (`OptionsValidation`). An invalid value stops the Service with the reason in the event log and `service.log`.

## `Agent` settings

| Key | Shipped value | Rule / meaning |
|---|---|---|
| `ApiBaseUrl` | `https://onexso.com:7229` | **Required**; absolute `https://` only. Set the UAT backend here |
| `PortalRootDomain` | `onexso.com` | Tenant domain accepted by browser activation |
| `PortalProductionPort` / `PortalDevelopmentPorts` / `PortalDevelopmentOrigins` | 443 / [4200] / dev origins | Accepted portal origins for activation; development ports/origins are honoured only in Development |
| `HeartbeatIntervalSeconds` | 30 | Device heartbeat |
| `PolicyRefreshIntervalSeconds` | 3600 | Policy fetch |
| `IngestIntervalSeconds` | 150 | Upload cadence |
| `QueueMaxRecords` / `QueueReservedRecords` | 5000 / 500 | Pending snapshot cap / headroom for lifecycle records |
| `QueueCapacityMb` | 100 | Database size limit |
| `HttpTimeoutSeconds` | 30 | Backend call timeout |
| `ForceMonitoringActive`, `AllowLocalLifecycleWithoutFullGates`, `AllowIpcClientFromAnyPath` | false | **Development only**; refused outside Development (AUD-013) |

## Environments

| Environment | How to select | Notes |
|---|---|---|
| Development | `DOTNET_ENVIRONMENT=Development` + `appsettings.Development.json` | Dev flags allowed; debug logging |
| UAT | Production environment, with `Agent__ApiBaseUrl` set to the UAT backend (machine environment variable or edited `appsettings.json` in the install dir) | Trust the UAT TLS chain and the signing certificate on devices |
| Production | Production environment, shipped `appsettings.json` | — |

The shipped `appsettings.json` points at `onexso.com:7229`. Confirm with the backend team that this is the intended target per environment (UAT-11). No secrets are in configuration files: the device token comes from activation and is stored by the Service.

## Tray

The tray has no backend configuration. It talks only to the Service over the pipe and writes its log to `%LocalAppData%\ONEVO\Agent\logs\tray.log`.

## Feature control

Collectors and capabilities are enabled by the server policy (`tray/policy`):
- activity, app usage and screenshots;
- inactivity screenshots, camera verification and location tracking;
- the idle threshold.

Until a policy arrives, the tray runs activity counts only (`CollectorCoordinator.CreateLocalDefaultPolicy`).
