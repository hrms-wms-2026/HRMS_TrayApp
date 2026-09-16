# TrayApp Terms &amp; Conditions Parity — Design Spec

**Date:** 2026-09-16
**Repos touched:** HRMS-Backend-v1, HRMS_TrayApp

## Problem

The web login flow already implements "only ask the user to accept terms
when the published version changed since their last acceptance":

- `LegalDocumentVersion` (published doc + version) and
  `LegalAcceptanceRecord` (per-user, per-version acceptance decision) —
  `HRMS-Backend-v1/src/ONEVO.Domain/Features/DevPlatform/Compliance/Entities/LegalDocumentVersion.cs`,
  `.../Features/Auth/Login/Entities/LegalAcceptanceRecord.cs`.
- `LegalAcceptanceChecker.CheckAsync(tenantId, userId)`
  (`HRMS-Backend-v1/src/ONEVO.Application/Features/Auth/Legal/Services/LegalAcceptanceChecker.cs:23`)
  compares currently-published required versions against the user's
  recorded acceptances and only returns `PendingDocuments` for versions the
  user hasn't accepted yet.
- `LoginContinuationService.cs:127` calls this and feeds
  `RequiresLegalAcceptance`/`PendingLegalDocuments` into `LoginResponseDto`.
- `login.component.ts:57` only navigates to `/auth/legal-consent` when
  `legalAcceptanceRequired` is true — otherwise login proceeds straight
  through.

The TrayApp has none of this. `AgentWorker.cs`/`OnevoApiClient.cs` never
reference legal/terms/consent, and `TrayAuthPayload`
(`OnevoApiClient.cs:744`) carries no equivalent fields. The tray's only
"consent" UI, `PrivacyTransparencyPage`/`PrivacyTransparencyViewModel`, is an
unrelated one-time device-monitoring disclosure with no version tracking —
it is not touched by this spec.

**Desired behavior:** the tray checks the same backend-tracked acceptance
state and only prompts when something is actually pending — never
unconditionally, and never silently skipped forever once a new version is
published.

## Out of scope

- `PrivacyTransparencyPage` stays exactly as it is today (separate feature).
- No new backend entity, endpoint, or migration — this wires the tray into
  the system that already exists for the web app.

## Flow

### 1. Surfacing the check at both places the tray receives credentials

The tray obtains/refreshes credentials at exactly two points, and both must
carry the acceptance state — enrollment alone isn't enough, because the tray
silently resumes from a 90-day refresh token, so a document published after
initial enrollment would otherwise never reach an already-enrolled user.

- `TrayEnrollmentService.IssueAsync`
  (`HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs:42`) —
  after resolving the employee identity, call
  `ILegalAcceptanceChecker.CheckAsync(request.TenantId, request.UserId, ct)`
  (the exact same interface `LoginContinuationService` already depends on —
  inject it into `TrayEnrollmentService`'s constructor).
- `RefreshTrayTokenCommandHandler.Handle`
  (`HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs:47`) —
  same call, using `existingToken.TenantId`/`existingToken.UserId` (already
  in scope at line 99's `ResolveEmployeeIdentityAsync` call site).

### 2. Response contract

Extend `TrayAuthResponseDto`
(`HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs`)
with two more fields, named to mirror `LoginResponseDto`'s existing
`legal_acceptance_required`/`pending_legal_documents` naming so the tray and
web payloads stay consistent:

```csharp
[property: JsonPropertyName("legal_acceptance_required")] bool RequiresLegalAcceptance = false,
[property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentDto> PendingLegalDocuments = null
```

(`PendingLegalDocumentDto` already exists — reuse it as-is, same shape the
web app consumes: `DocumentType`, `Version`, `Title`, `EffectiveAt`,
`ContentUrl`, `ContentEndpoint`, `ContentHash`.)

Both `IssueAsync` and `RefreshTrayTokenCommandHandler` populate these from
`LegalAcceptanceChecker.CheckAsync`'s result the same way
`LoginContinuationService` already does.

### 3. TrayApp — plumbing the new fields through

- `TrayAuthPayload` (`HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs:744`) —
  add `RequiresLegalAcceptance`/`PendingLegalDocuments` (matching field
  names/JSON tags to the backend DTO) so deserialization picks them up on
  every enroll/exchange/refresh response.
- `AgentWorker.cs` — after a successful enroll or a successful silent
  refresh, if `RequiresLegalAcceptance` is true, surface this to the UI
  layer (mirroring how it already surfaces `EmployeeProfileStatus ==
  "company_context_required"` as a distinct state today) instead of
  proceeding straight to the normal connected/resumed state.

### 4. TrayApp — new pending-acceptance screen

- New `LegalConsentPage`/`LegalConsentViewModel`, visually consistent with
  `PrivacyTransparencyPage` (same card/list style already established in
  that file) — but functionally simple: list `PendingLegalDocuments` (title
  + version), each with a link/button that opens `ContentUrl` (or fetches
  `ContentEndpoint` if `ContentUrl` is absent — same fallback the web
  consent component already implements) so the employee can actually read
  the document, plus a single "Accept and continue" action.
- "Accept and continue" posts to the existing endpoint
  `POST /api/v1/legal/acceptances/complete-login`
  (`HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Legal/AuthPendingLegalController.cs:24`) —
  no new backend endpoint. On success, the tray proceeds into its normal
  connected state (same place it would have gone if
  `RequiresLegalAcceptance` had been false).
- If `RequiresLegalAcceptance` is false — the common case on every ordinary
  login/refresh once a user is caught up — this screen is never shown, same
  as `login.component.ts:57`'s behavior on the web.

## Error handling / edge cases

- **Network failure while posting acceptance:** show a retry-capable error
  on `LegalConsentPage` (same pattern as `legal-consent.component.html`'s
  `store.error()` block) — do not silently drop into the connected state
  without a successful accept when the backend said acceptance is required.
- **Multiple pending documents:** the employee must accept all of them in
  one action (same as the web flow — `complete-login` accepts the whole
  pending set in one call), not one at a time.
- **Refresh-token path specifically:** if `RequiresLegalAcceptance` comes
  back true on a silent background refresh (app already running, no user
  present at a screen), the tray brings the app to the foreground /
  surfaces a notification rather than silently blocking background
  functionality — exact UX for "user not looking at the tray right now"
  needs a follow-up decision at implementation time if it isn't already
  covered by how `AgentWorker` handles the existing
  `company_context_required` state (check that pattern first; reuse it).

## Testing

- Backend unit tests: `TrayEnrollmentService.IssueAsync` and
  `RefreshTrayTokenCommandHandler.Handle` — verify
  `RequiresLegalAcceptance`/`PendingLegalDocuments` populate correctly for
  (a) no pending documents, (b) one pending document, mirroring the
  existing `LegalAcceptanceChecker` test fixtures already used for the web
  login path.
- TrayApp unit tests: `TrayAuthPayload` deserializes the two new fields;
  `AgentWorker` routes to the pending-acceptance state when
  `RequiresLegalAcceptance` is true and skips it when false, for both the
  enroll and refresh code paths.
- TrayApp unit tests: `LegalConsentViewModel` — accept action calls the
  correct endpoint with all pending document identifiers and transitions to
  connected state only on success.
