# 02 — Security Review (T03)

Scope: token lifecycle, local secrets, TLS, backend authorization, IPC trust, user/tenant isolation, privacy-sensitive logging. Backend code was not available, so server-side enforcement is listed as manual verification.

## Controls present on `develop` (verified in code)

| Area | Control | Where |
|---|---|---|
| Device token storage | DPAPI (LocalMachine) file in `%ProgramData%\ONEVO\Agent`, folder restricted to the Service accounts | `CredentialStore`, AUD-003 |
| Token lifecycle | Activation exchange/refresh/revoke; proactive refresh before expiry; refresh on 401 with single-flight; transient errors keep credentials; rejection clears credentials and locks | `DeviceTokenRefresher`, `TokenRefreshService`, `DeviceTokenRefreshTests` |
| Token in tray | The tray never holds the device JWT; all backend calls go through the Service | Architecture |
| Employee isolation on shared PCs | Each queued row is stamped with the owner (`tenant:user`) and is never uploaded under another employee's token | AUD-004, `ActivitySyncService` |
| IPC client trust | The client must be the installed `ONEVO.Agent.TrayApp.exe` (install dir or MSIX `WindowsApps`) running in the active console session | `IpcClientVerifier`; tests cover path traversal and another user's session |
| IPC robustness | Bounded line reader; handshake on the shared reader; BOM-less writers | `BoundedLineReader`, `NamedPipeAuthenticator` |
| TLS | `ApiBaseUrl` must be an absolute `https://` URL; the Service refuses to start otherwise | `OptionsValidation` |
| Dev bypass flags | `ForceMonitoringActive`, `AllowLocalLifecycleWithoutFullGates`, `AllowIpcClientFromAnyPath` refused outside Development | AUD-013 |
| Browser activation | Portal origin validated against `PortalRootDomain` / ports | `PortalOriginValidator` |
| Evidence (screenshots, face photos) | Spooled DPAPI-encrypted, deleted after delivery | `EvidenceSpoolStore`, `DpapiEvidenceProtector` |
| Live GPS | Not persisted beyond its day | AUD-019 |
| Signing | No default certificate password in `build-msix.ps1` | AUD-024 |
| Logging | Correlation IDs, no tokens logged | AUD-018 |

## Findings

| ID | Severity | Finding |
|---|---|---|
| UAT-03 | P3 (privacy, policy) | Per-minute keyboard/mouse counts are persisted to the employee's `tray.log` (Information level). Needs a monitoring-policy decision: log at Debug or drop the counts. |
| UAT-09 | P3 | DPAPI **LocalMachine** scope means confidentiality relies entirely on the folder ACL (AUD-003). Acceptable with the ACL. Verify on a device that standard users cannot read `credential.dat` (11 §F). |
| UAT-10 | Info | `release.yml` signs with a PFX from secrets; the publisher must match `CN=ONEVO` in the manifest. A production code-signing certificate from a trusted CA is a T17 prerequisite; the dev self-signed cert needs `TrustedPeople` import on every device. |

## Manual / backend verification required

- The backend rejects expired, revoked or foreign-tenant device tokens (401/403), and derives tenant/employee only from the token.
- The UAT backend TLS certificate chains to a root trusted on managed devices.
- Pen test of the browser-activation protocol handler (`onexso-workspace:`) with a crafted origin.
- On a device: a standard user cannot read `%ProgramData%\ONEVO\Agent\*`, and a copy of `ONEVO.Agent.TrayApp.exe` run from Downloads is refused by the Service.
