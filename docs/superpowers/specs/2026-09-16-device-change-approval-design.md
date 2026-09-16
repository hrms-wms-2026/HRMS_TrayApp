# Device Change Approval — Design Spec

**Date:** 2026-09-16
**Repos touched:** HRMS-Backend-v1, HRMS_TrayApp, Hrms--Web-application---front-end---v1

## Problem

Today there is no "one approved device per employee" enforcement anywhere in the
system:

- `TrayDeviceRegistration` (`HRMS-Backend-v1/src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/TrayDeviceRegistration.cs`)
  has only non-unique indexes — nothing stops a second row for the same
  `(TenantId, UserId)` with a different `DeviceFingerprint`.
- `TrayEnrollmentService.IssueAsync` (`.../Services/TrayEnrollmentService.cs:42`)
  unconditionally inserts a new registration on every successful pairing —
  browser-approval (`PollDeviceAuthorizationCommandHandler`) and manual
  activation code (`ExchangeActivationCodeCommandHandler`) both call it.
- The only fingerprint check that exists, in `RefreshTrayTokenCommandHandler.cs:63`,
  only detects refresh-token replay on a device's *own* registration row — it
  does nothing to stop a second, independently-enrolled device.
- No TrayApp UI, and no admin workflow, exists for "this device is not
  approved."

**Desired behavior:** an employee has exactly one active approved device at a
time. Enrolling from a different device is blocked, and automatically creates
a request to the employee's resolved approver (reporting line / position
coverage / department coverage — the existing "reporting coverage" concept).
The request appears in the existing web Requests screen. Approving it retires
the old device and lets the new one enroll.

## Out of scope

- Changing how the *first* enrollment or same-device re-activation works —
  both are unaffected.
- Admin-initiated device revocation/inventory management (not requested).
- Any change to `RefreshTrayTokenCommandHandler`'s existing
  fingerprint-replay check — it stays as is.

## Data model

New entity, modeled directly on the existing `LocationChangeRequest`
(`HRMS-Backend-v1/src/ONEVO.Domain/Features/TimeAttendance/Entities/LocationChangeRequest.cs`)
so it follows the same status-string / review-fields shape already used by
the three other request types on the Requests screen:

```csharp
namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public sealed class DeviceChangeRequest : ITenantOwnedEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }          // = UserId on TrayDeviceRegistration
    public Guid? LegalEntityId { get; set; }
    public Guid? CurrentDeviceRegistrationId { get; set; } // null if user somehow has none
    public string NewDeviceFingerprint { get; set; } = string.Empty;
    public string NewDeviceName { get; set; } = string.Empty;
    public string NewDeviceOs { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
}
```

Unique-ish constraint via app logic, not DB: at most one `pending` row per
`(TenantId, EmployeeId)` — a repeat enrollment attempt from the same new
fingerprint while a request is already pending updates `RequestedAt` on the
existing row rather than inserting a duplicate (see Flow, step 3).

Table: `device_change_requests`, tenant-scoped like the entity it's modeled
on (same RLS pattern as `location_change_requests`).

## Flow

### 1. Detecting the mismatch (backend, single choke point)

The guard lives in `TrayEnrollmentService.IssueAsync`
(`HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs:42`),
*before* the existing `AddDeviceRegistrationAsync` call — this is the one
method both `PollDeviceAuthorizationCommandHandler.cs:86` (browser-approval
path) and `ExchangeActivationCodeCommandHandler.cs:56` (manual-code path)
already call, so a guard here covers both without duplicating logic in each
handler.

```
existing = repository.FindLatestActiveDeviceForUserAsync(userId, tenantId)  // already exists, ITrayActivationRepository.cs:36

if existing is null:
    proceed normally (first enrollment)
elif existing.DeviceFingerprint == request.DeviceFingerprint:
    proceed normally (re-activation of the same device)
else:
    // different device — block and raise a request instead of issuing credentials
    upsert a DeviceChangeRequest (pending) for (tenantId, userId):
        - if a pending row already exists for this user, update its
          NewDeviceFingerprint/NewDeviceName/NewDeviceOs/RequestedAt in place
          (handles repeated retry-connect attempts from the tray without
          spamming duplicate rows/approver notifications)
        - else insert a new pending row, CurrentDeviceRegistrationId = existing.Id
    throw a new DeviceChangePendingException (or return a sentinel the two
    callers translate to Failure("device_change_pending", ...) — match
    whichever error-propagation style IssueAsync's return type already
    supports; it currently returns TrayAuthResponseDto directly, so the
    cleanest fit is a typed exception the two handlers catch, since IssueAsync
    has no Result<T> wrapper today)
```

`ExchangeActivationCodeCommandHandler.cs:49`'s existing
`RevokeActiveRegistrationsForIdentityAsync` call happens *before* `IssueAsync`
and only revokes registrations matching the same fingerprint (re-activation
cleanup) — it is unrelated to this guard and stays unchanged.

### 2. Handlers propagate the block

- `PollDeviceAuthorizationCommandHandler.Handle` (`PollDeviceAuthorizationCommandHandler.cs:86`):
  catch the exception from `IssueAsync`, return
  `Failure("device_change_pending", "A different device is already approved for your account. A request has been sent for approval.")`
  instead of `Result<TrayAuthResponseDto>.Success(...)`. The authorization
  row still transitions to `Consumed` (the pairing session is spent either
  way — retrying needs a fresh code/session).
- `ExchangeActivationCodeCommandHandler.Handle` (`ExchangeActivationCodeCommandHandler.cs:32`):
  same catch, same `Failure("device_change_pending", ...)`, activation code
  stays marked used (matches existing behavior for other failure cases in
  this handler).

### 3. Approver resolution and the Requests screen

- Add `DeviceChangeApproval` to `EmployeeAuthorityPurpose`
  (`HRMS-Backend-v1/src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs`)
  — per the enum's own doc comment, adding a value needs no migration.
- New workflow service `DeviceChangeRequestWorkflow` (mirrors
  `LocationChangeRequestWorkflow.cs`) calls
  `IEmployeeAuthorityResolver.ResolveApproverAsync` with
  `EmployeeApprovalRouteRequest(SubjectEmployeeId: employeeId, LegalEntityId: ..., RequiredPermission: "attendance:approve", Purpose: EmployeeAuthorityPurpose.DeviceChangeApproval)`
  to resolve who can see/approve the request. No new approver logic.
- New controller `DeviceChangeRequestsController` (mirrors
  `LocationChangeRequestsController.cs`): list pending (scoped to what the
  caller is authorized to see, via the same authority resolver), approve,
  reject. Guarded by `attendance:approve` — the same permission the other
  three Requests tabs already require, confirmed in the design discussion as
  the correct gate (whoever approves location/correction/work-area requests
  also approves device-change requests).
- **On approve:**
  1. `repository.DeactivateDeviceAsync(request.CurrentDeviceRegistrationId, now)` (exists, `ITrayActivationRepository.cs:38`)
  2. `repository.RevokeAllRefreshTokensForDeviceAsync(request.CurrentDeviceRegistrationId, "device_change_approved")` (exists, line 33)
  3. `request.Status = Approved; ReviewedById = approverId; ReviewedAt = now`
  4. `SaveChangesAsync`

  This does **not** mint new credentials — the original pairing
  session/activation code that triggered the block has already been consumed
  or expired. The employee must retry "Connect" in the tray. On that retry,
  `IssueAsync`'s guard (step 1) now finds no active device for the user (it
  was just deactivated) and proceeds as a first enrollment, issuing fresh
  credentials for the new fingerprint normally.
- **On reject:** `request.Status = Rejected; ReviewComment = ...`. The
  employee stays blocked; a later "Connect" attempt from the same new
  fingerprint creates a **new** pending request (the old rejected row is not
  reused — an explicit re-request is a deliberate, visible new event for the
  approver rather than silently resurrecting a denied one).

### 4. Frontend — new Requests tab

`time-tracking.component.ts` (`Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/time-tracking/time-tracking.component.ts:94`)
currently types `approvalType` as `'corrections' | 'work-area' | 'location'`.
Add `'device-change'` to the union and a fourth tab button, following the
exact same per-tab pattern as the existing three:

- New `DeviceChangeApprovalsComponent` (own component, own store
  `DeviceChangeRequestsStore`, own `device-change-request-api.service.ts`) —
  mirrors `LocationChangeApprovalsComponent`/`LocationChangeRequestsStore`/`location-change-request-api.service.ts`
  file-for-file in structure.
- List view: employee name, current device name/OS, requested (new) device
  name/OS, requested-at. Approve/Reject actions call the new controller's
  endpoints.
- Route guard/permission: unchanged — the existing `attendance:approve` route
  data on `attendance.routes.ts` already covers the whole tabbed screen, no
  per-tab permission split exists today and none is being introduced.

### 5. TrayApp UX

- `OnevoApiClient.cs` poll/exchange result mapping (`OnevoApiClient.cs:98`
  area) — add a case for `"device_change_pending"` alongside the existing
  `authorization_pending` / `slow_down` / `expired_token` / `access_denied`
  mappings, surfaced through the same `DeviceAuthorizationPollState`-style
  result to the ViewModel layer (for the browser-approval poll path); the
  manual-activation-code path's result type gets the equivalent new error
  code string.
- `ConnectWorkspaceViewModel.cs`: add `"DEVICE_CHANGE_PENDING"` to both
  switch expressions that map `ErrorCode` to `ErrorMessage`
  (`VerifyAndConnectAsync`'s block at line ~92 and
  `HandleDevicePairingResult`'s block at line ~197), message:
  *"A different device is already approved for your account. We've sent a
  device-change request to your approver — you'll be notified once it's
  approved."*
- No polling loop added on the tray side for request status — the employee
  retries "Connect"/"Connect via Browser" manually after being notified (out
  of band — e.g. told by their approver), consistent with how every other
  failure on this screen already works (user-initiated retry, not
  auto-retry).

## Error handling / edge cases

- **Fingerprint stability:** `DeviceFingerprint.Compute()`
  (`HRMS_TrayApp/ONEVO.Agent.Service/Security/DeviceFingerprint.cs:15`) hashes
  the Windows-level `MachineGuid` from
  `HKLM\SOFTWARE\Microsoft\Cryptography`, falling back to `MachineName` only
  if that registry read fails. `MachineGuid` survives app reinstalls and
  even most OS upgrades in place — it only changes on a full OS
  reimage/reinstall, which is a legitimate "this is now a different device"
  case. No design change needed here; confirmed safe.
- **Employee self-logout then reconnect on a new device:** self-revocation
  (`RevokeDeviceCommandHandler`) deactivates the device's own registration.
  After that, `FindLatestActiveDeviceForUserAsync` finds nothing active, so
  the next enrollment (from any device) is treated as a first enrollment —
  no block, no request. This is intentional: an explicit sign-out is treated
  as "I'm done with that device," matching the existing self-service
  sign-out semantics; it is not a gap this spec needs to close.
- **Naming collision avoided:** an unrelated existing error,
  `AgentCommandDeviceMismatch` (`HRMS-Backend-v1/src/ONEVO.Domain/Errors/MonitoringErrors.cs:50`),
  fires when a remote command (e.g. a screenshot request) targets a
  registration ID that doesn't match the completing device — a per-command
  ownership check, unrelated to this feature. The new error code
  `device_change_pending` is deliberately distinct.
- **No active device at all with a mismatch somehow both true:** not
  possible per the flow above — "no active device" and "mismatched
  fingerprint" are mutually exclusive branches.

## Testing

- Backend unit tests: `TrayEnrollmentService` — first enrollment (no
  existing device) issues credentials; same-fingerprint re-activation issues
  credentials; different-fingerprint creates/upserts a pending
  `DeviceChangeRequest` and does not issue credentials.
- Backend unit tests: `DeviceChangeRequestWorkflow` approve path deactivates
  the old device, revokes its tokens, and marks the request approved; reject
  path leaves the device active and marks the request rejected.
- Backend integration test: full poll-flow — enroll device A, attempt
  enroll device B (expect `device_change_pending`, no new
  `TrayDeviceRegistration` row), approve via the workflow, retry enroll
  device B (expect success, device A now inactive).
- Frontend: `DeviceChangeApprovalsComponent`/store — list, approve, reject,
  following the same test shape as `LocationChangeApprovalsComponent`'s
  spec.
- TrayApp: `ConnectWorkspaceViewModel` tests — `DEVICE_CHANGE_PENDING` error
  code maps to the expected `ErrorMessage`, for both the manual-code and
  browser-approval paths.
