# 02 — Authentication & Security Review (T03)

Scope: token lifecycle, local secrets, TLS, backend authorization, user/tenant isolation, IPC, privacy-sensitive logging.
Revision audited: `5cca93e`. Status columns are kept current in `03-issue-register.md`.

## 1. Summary

| Area | Finding | Severity | Issue |
|---|---|---|---|
| Token lifecycle | No login, enrollment, token acquisition, expiry handling or refresh exists. `CredentialStore.StoreDeviceJwt` has no caller. | Blocker (functional) | ISS-002 |
| Local secret storage | Device JWT encrypted with DPAPI **LocalMachine** scope (decryptable by *any* process on the machine) in a `%ProgramData%` sub-folder that inherits `BUILTIN\Users: Read` → any standard local user can read and decrypt the device token. `Users` also have create-folder rights in `%ProgramData%`, so a user can pre-create `ONEVO\Agent` before install and own it (squatting/tampering). | **P1** | ISS-010 |
| Secret file integrity | `File.WriteAllBytes` / `WriteAllText` in place — a crash mid-write corrupts the token/identity file. | P2 | ISS-024 |
| IPC authentication | The "nonce authentication" is an echo: the server sends a random nonce and accepts any client that sends the same value back. It proves nothing about the client. The pipe ACL grants `InteractiveSid` read/write, so **any process of any interactive user** can connect, read state and (once implemented) submit records. | **P1** | ISS-011 |
| IPC server spoofing | The tray never verifies the server's identity and the server does not use `FirstPipeInstance`. If the service is stopped, any user process can create `ONEVO.Agent.Pipe.v1` and receive the tray's activity data or push a forged `PolicyPush` / state. | **P1** | ISS-011 |
| IPC resource exhaustion | `ReadLineAsync` buffers a whole line before the 64 KB size check → a local process can make the SYSTEM service allocate unbounded memory. Unlimited server instances. | P2 | ISS-020 |
| TLS | `ApiBaseUrl` is not validated: `http://` would be accepted and the Bearer token sent in clear text. No HttpClient is registered, so `HttpTimeoutSeconds` is never applied. No certificate pinning (acceptable for UAT if the OS trust store is managed). | P2 | ISS-019 |
| Backend authorization / tenant isolation | Client correctly sends **no** `tenant_id`; tenant is derived from the device JWT server-side (`ActivitySyncService` doc comment). Server-side enforcement could not be verified — backend source is not in scope/available. | Untested | — (manual backend test) |
| User isolation | One device JWT per machine. On multi-user machines (fast user switching, RDS) every interactive user's tray connects to the same service; records carry no user identity, so activity of different employees is attributed to the same device/employee. | P2 (escalate) | ISS-022 |
| Privacy-sensitive logging | Good: counts and process names are deliberately not logged; hooks never read key codes or coordinates; window titles/paths never collected. Tokens are never logged. `ex.Message` from IO errors is logged (no sensitive content observed). | OK | — |
| Employee termination of monitoring | Tray "Exit" menu lets the employee stop the tray (and therefore activity capture). Needs a documented policy decision. | Policy | ISS-034 |
| Dependencies | No known vulnerable packages reported by NuGet for Service/Shared/tests. Runtime packages are at 10.0.0 (servicing 10.0.14 available). | P3 | ISS-029 |

## 2. Detail — local credential exposure (ISS-010)

`CredentialStore.cs:16`:

```csharp
ProtectedData.Protect(Encoding.UTF8.GetBytes(jwt), null, DataProtectionScope.LocalMachine);
```

DPAPI `LocalMachine` scope protects data **from other machines only**; every user and process on the same machine can call `Unprotect`. Confidentiality therefore depends entirely on the file ACL. `Directory.CreateDirectory` under `%ProgramData%` inherits the default `%ProgramData%` ACL (`BUILTIN\Users:(OI)(CI)(RX)` and `BUILTIN\Users:(CI)(WD,AD,WEA,WA)`), so the file is readable by all users and the folder is writable/creatable by them.

Impact: a local user (or malware running as that user) obtains the device JWT and can submit forged activity for the device's tenant/employee, or read whatever the backend exposes to device tokens.

Remediation (implemented, see register): create the folder with a **protected** ACL granting only `SYSTEM` and `Administrators`, re-apply it if the folder already exists (removes squatter's rights), add entropy to DPAPI, and write files atomically (temp file + replace).

## 3. Detail — IPC trust model (ISS-011)

Recommended design (needs human approval because it changes the IPC contract and requires Windows testing):

1. Server: create the first instance with `PipeOptions.FirstPipeInstance`; on each connection call `GetNamedPipeClientProcessId` and verify the client image path is the installed `ONEVO.Agent.TrayApp.exe` and its session is an active console/RDP session; tag records with that session's user SID.
2. Client: call `GetNamedPipeServerProcessId` and verify the server process runs as `LocalSystem` from the installed service path before sending data or trusting state/policy.
3. Replace the echo nonce, or remove it — it adds no security and gives a false sense of authentication.

## 4. Items requiring backend/manual verification

- Backend rejects ingest without/with an expired/foreign-tenant JWT (401/403) — no backend available to this audit.
- Backend derives tenant/employee from the JWT and ignores any client-supplied identifiers.
- TLS configuration of the UAT endpoint (certificate chain trusted on managed devices).
