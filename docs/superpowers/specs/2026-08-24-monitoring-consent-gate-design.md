# Monitoring Consent Gate (WFM-11) — Design

Date: 2026-08-24
Related: `C:\HR\docs\WFM-R1-Scope-Decisions.md` (WFM-11), `[[project_onevo_wfm_r1_scope]]`

## Problem

`LifecycleGate` already models `ConsentValid` as 1 of the 9 conditions required for `MonitoringState.Active` (`ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs`). But `AgentWorker.ApplyEnrollmentGates()` hardcodes `SetConsentValid(true)` immediately after login, with a comment admitting consent capture "is not yet built (§23 gap)." The TrayApp's `PrivacyConsentPage` (TA-POL-01) shows toggles for what's monitored, but they're display-only and locked, and "Allow and Continue" just navigates to `//clockin` — it records nothing anywhere. The backend has no consent-related domain entity at all. Given biometric face-scan clock-in is already live in production, this is live legal exposure, not a cosmetic gap.

## Scope decisions (settled before this design)

- **One bundled consent**, not per-capability. A single "I agree to be monitored while clocked in" covers activity counts, app usage, and biometric clock-in — matches the existing single `ConsentValid` gate.
- **Backend is the source of truth.** Not device-local DPAPI. Survives reinstalls, works across devices, gives HR/Legal an audit trail.
- **Versioned.** A separate `ConsentTextVersion` (not reused from `AgentPolicy.Version`, which changes for unrelated settings like screenshot interval) forces re-consent only when the actual disclosure text changes.
- **Fixed default text for R1.** No admin-editable consent text screen; `CurrentVersion`/text are code constants. Editable-per-tenant is Phase 2.
- **Mandatory accept, no Decline button.** Consistent with fail-closed design — closing the app without accepting just means the employee never reaches `Active`.

## Architecture

```
Backend: EmployeeMonitoringConsent (new entity, tenant-owned)
   ↑ read/write
GetEffectiveTrayPolicyQueryHandler → TrayAgentPolicyDto
   + ConsentTextVersion, ConsentText, HasValidConsent
   ↓ (existing PolicySyncService cadence: 45 min + immediate on JWT)
PolicySyncService.RefreshOnceAsync
   → LifecycleGate.SetConsentValid(policy.HasValidConsent)
   → broadcasts PolicyPush over IPC (existing, unchanged)
   ↓
TrayApp: PrivacyConsentViewModel reads ConsentText/Version from LastKnownPolicy
```

This piggybacks on the existing policy poll (Approach A) instead of a separate consent subsystem — no new background poll loop, no new push message type for the read side. The only new IPC traffic is the write side: submitting acceptance.

## Components

### Backend (`HRMS-Backend-v1`)

- **New domain entity** `EmployeeMonitoringConsent`: `Id`, `TenantId`, `EmployeeId`, `ConsentTextVersion`, `AcceptedAt`. Tenant-owned, same RLS pattern as other tenant tables. One row per employee, upserted on accept (latest version wins).
- **New constant** `MonitoringConsentText` (Application layer): `CurrentVersion = "1"`, `Text = "<fixed disclosure>"`.
- **Extend** `TrayAgentPolicyDto` / `GetEffectiveTrayPolicyQueryHandler`: add `ConsentTextVersion`, `ConsentText`, `HasValidConsent` (computed by comparing the employee's stored `EmployeeMonitoringConsent.ConsentTextVersion` to `MonitoringConsentText.CurrentVersion`).
- **New endpoint** `POST /api/v1/monitoring/tray/consent` — body `{ ConsentTextVersion }`, authenticated via Device JWT (same `ITrayCurrentDevice` pattern as `GetEffectiveTrayPolicyQueryHandler`). Upserts `EmployeeMonitoringConsent`. No separate GET endpoint — policy already carries current status.

### Shared (`ONEVO.Agent.Shared`)

- `AgentPolicy` record: add `ConsentTextVersion`, `ConsentText`, `HasValidConsent`.
- New IPC message pair in `IpcMessageTypes`: `ConsentAcceptSubmit` / `ConsentAcceptResult`, with payload records `ConsentAcceptSubmitPayload { ConsentTextVersion }` and `ConsentAcceptResultPayload { Success, ErrorCode, Message }` — same shape convention as the existing `BiometricEnrollment*` pairs.

### Service (`ONEVO.Agent.Service`)

- `AgentWorker.ApplyEnrollmentGates()`: **delete** the hardcoded `SetConsentValid(true)`. Field defaults to `false` (fail-closed) until the first real policy fetch resolves it — `PolicySyncService` already fetches immediately once a JWT is available, so this adds no meaningful onboarding delay. (`ApplyDevBootstrapIfConfigured`'s hardcode stays — it's an explicit, logged, dev-only escape hatch, unrelated to this gap.)
- `PolicySyncService.RefreshOnceAsync`: after `_policyCache.Set(policy)`, call `_lifecycleGate.SetConsentValid(policy.HasValidConsent)`.
- `OnevoApiClient`: add `AcceptMonitoringConsentAsync(deviceJwt, version, ct)` calling the new POST endpoint.
- `AgentWorker`: handle new `ConsentAcceptSubmit` IPC message — call `OnevoApiClient.AcceptMonitoringConsentAsync`; on success, immediately re-run `PolicySyncService.RefreshOnceAsync` (don't wait for the 45-min cycle) so the gate flips without delay, then reply `ConsentAcceptResult`.
- `ExecuteClockIn` / `ExecuteEndBreak`: when `CanActivate` is false, inspect `LifecycleGate.Snapshot()`. If `!snapshot.ConsentValid`, return error code `CONSENT_REQUIRED` (specific) instead of the generic `GATES_CLOSED`, so the TrayApp can route intelligently.

### TrayApp (`ONEVO.Agent.TrayApp`)

- `PrivacyConsentViewModel`: bind `ConsentText`/`ConsentTextVersion` from `_pipe.LastKnownPolicy` (real values, replacing the current toggle-only display). `AllowAndContinue` now calls a new `INamedPipeClient.AcceptConsentAsync(version, ct)`, awaits the result, and navigates to `//clockin` only on success; on failure, sets `ErrorMessage` and stays on the page (fail-closed — no silent bypass).
- `INamedPipeClient` / `NamedPipeClient`: add `AcceptConsentAsync`, mirroring the existing `CompleteBiometricEnrollmentAsync` pattern.
- `ClockInViewModel.ClockInAsync`: when `result.ErrorCode == "CONSENT_REQUIRED"`, navigate to `//policy` instead of just surfacing the raw error text.
- `ActiveSessionViewModel`'s break-resume path (`EndBreak`): same `CONSENT_REQUIRED` check and routing, since it hits the identical gate.

## Data flow — re-consent on a version bump

1. HR/Legal changes the disclosure wording → a code change bumps `MonitoringConsentText.CurrentVersion` (e.g. "1" → "2"). No admin UI for this in R1.
2. Every employee's stored consent (`ConsentTextVersion = "1"`) no longer matches. Next policy poll for each of them returns `HasValidConsent = false`.
3. `PolicySyncService` sets `LifecycleGate.SetConsentValid(false)` for anyone currently not `Active` (their next `ClockIn` fails gate check) and for anyone who is `Active`, nothing forces them out — the gate is only evaluated at `ClockIn`/`EndBreak` transitions, consistent with how the other 8 gates already behave. No new mid-session kick-out logic.
4. Their next `ClockIn` or break-resume attempt returns `CONSENT_REQUIRED`; the TrayApp routes them to `//policy`, which now shows version "2" text; accepting records it and clears the gate.

## Error handling

- Policy fetch failure (network, backend down): `PolicySyncService` already keeps the last-known-good policy (`GetEffectivePolicyAsync` failure path is a no-op). Consent status also stays at its last known value — fail-closed if that value was never successfully fetched (defaults to `false`), fail-open-on-stale-good-value only within `PolicyCache`'s existing `ValidUntil` expiry rules (unchanged behavior, same as every other policy flag).
- Consent accept call fails (network, backend down, validation error): `ConsentAcceptResult.Success = false`; TrayApp shows the error and does not navigate. Employee can retry.
- Race: employee accepts on Device A while Device B's policy poll is mid-flight for the same employee — harmless, both converge to `HasValidConsent = true` on their next poll; upsert semantics mean no duplicate-row conflict.

## Testing

- **Backend**: `GetEffectiveTrayPolicyQueryHandler` — `HasValidConsent` true/current-version, false/no-record, false/stale-version paths. Accept-command test — upserts correctly, tenant-isolated (employee in tenant A cannot write/read tenant B's record).
- **Service**: extend `LifecycleGateTests` (already has `SetConsentValid` coverage) for the `Snapshot().ConsentValid` inspection path; extend `PolicySyncServiceTests` to assert `SetConsentValid` is called with the fetched policy's `HasValidConsent`; new case in the lifecycle-command tests for `CONSENT_REQUIRED` on both `ClockIn` and `EndBreak`.
- **TrayApp**: `PrivacyConsentViewModelTests` — accept sends the IPC submit, navigates only on success, shows error and stays on failure; `ClockInViewModelTests` — routes to `//policy` on `CONSENT_REQUIRED` instead of just displaying it as a generic error.

## Out of scope (this piece of work)

- Admin UI to edit consent text per tenant (Phase 2).
- Explicit Decline button / consent-refusal flow.
- Forced mid-session kick-out when consent lapses while already `Active` — checked only at `ClockIn`/`EndBreak`, matching existing gate behavior.
- Screenshot-specific consent language (WFM-10 screenshots are out of scope for R1 entirely — this consent text covers what R1 actually collects: activity counts, app usage, biometric clock-in).
