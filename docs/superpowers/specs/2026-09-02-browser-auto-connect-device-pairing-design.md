# Browser Auto-Connect (Device Pairing) — Design

> **Revision note (2026-09-02, same day):** The original version of this spec designed a
> new `device/start`/`approve`/`deny`/`poll` mechanism from scratch. Before planning the
> implementation, a cross-repo sweep found that this exact feature — RFC 8628 OAuth
> Device Authorization Grant, applied to tray activation — was already built once and left
> unfinished:
> - **Backend**: a complete, unit-tested `MonitoringDeviceAuthorizationController` (start /
>   preview / approve / token) exists on `origin/development`, not on the branch this repo's
>   work was based on.
> - **TrayApp**: `OnevoApiClient.StartDeviceAuthorizationAsync` / `PollDeviceAuthorizationAsync`
>   already exist in the current tree, field-for-field matching the backend's DTOs — but
>   nothing calls them.
> - **Frontend**: `DeviceActivationComponent` + `DeviceAuthorizationApiService` already exist
>   in the current tree — but are wired into no route on any branch.
>
> This revision replaces the custom endpoint design below with "finish wiring up the
> existing implementation." The flow, security rationale, and non-goals are otherwise
> unchanged from the original version.

## Problem

The TrayApp's "Connect OneXso Workspace" screen ([ConnectWorkspaceViewModel.cs](../../../ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs))
requires the user to generate an 8-character activation code on the web portal, copy it,
switch back to the desktop app, and paste it in. The ask is a second, additive connect
path: click "Open Activation Website" (or a new "Connect via Browser" action), approve in
the browser — auto-skipping login if the browser already has a valid session, exactly like
`gh auth login --web` — and the desktop app finishes connecting on its own. **The existing
paste-code flow (`VerifyAndConnectAsync`, `PasteActivationCodeAsync`) is not touched.**

## Non-goals

- Changing or removing the manual code entry flow.
- A custom URI protocol callback (`onexso-workspace://`). It exists in the Windows
  manifest and a registration script, but nothing parses it, and it only works once a
  user has re-registered it as Administrator — too fragile. Polling (already built)
  works identically in dev and prod with no extra install step.
- Building a new pairing mechanism. The existing `MonitoringDeviceAuthorizationController`
  is complete, tested, and already RFC 8628-shaped — this spec wires it up, it doesn't
  replace it.

## Flow (as already implemented backend-side)

```
TrayApp (AgentWorker)              Backend                        Browser
   |--POST /device-authorization/start-->|  create TrayDeviceAuthorization
   |<--device_code,user_code,             |  (Pending, 10 min TTL)
   |    verification_uri_complete---------|
   |--Process.Start(verification_uri_complete)-------------------->  https://localhost:4200/device/activate
   |                                      |                          ?request_id=..&user_code=..
   |                                      |                          authGuard: not logged in?
   |                                      |                          -> /auth/login?returnUrl=...
   |                                      |                          -> back here once authenticated
   |                                      |<--GET /{requestId}?user_code=--  (TenantPolicy) preview
   |                                      |-->{device_name, device_os, ...}-|  shows "Approve <device>?"
   |                                      |<--POST /device-authorization/approve--  user clicks Accept
   |--POST /device-authorization/token--->|  {request_id, user_code}
   |   (device_code, device_fingerprint)  |  (TenantPolicy)
   |<--TrayAuthResponseDto (access/       |  status=Approved -> issues tokens directly
   |    refresh tokens + employee info)---|  (ITrayEnrollmentService.IssueAsync),
   |                                      |  marks Consumed
```

Poll (`/token`) returns the **full auth payload directly** — access token, refresh token,
employee identity — not an intermediate activation code. `AgentWorker`'s success path on a
completed poll is therefore `PersistAuth → SendHeartbeatAsync → TryTransition(Stopped) →
ApplyEnrollmentGates → reply`, the same tail `HandleActivationCodeSubmitAsync` already runs
after `ExchangeActivationCodeAsync` — it does not call `ExchangeActivationCodeAsync` itself.

Poll failure codes (already implemented, RFC 8628-standard): `authorization_pending`
(keep polling), `slow_down` (client polled faster than `interval_seconds`, back off),
`expired_token`, `access_denied`.

### Security separation

`user_code` (8 chars, human-facing) travels in the browser URL and is what the approval
screen shows/uses. `device_code` (32-byte opaque token) is returned only to the TrayApp
and is what `/token` polls with, alongside `device_fingerprint` — never exposed in the
browser or URL. This is the RFC 8628 `device_code`/`user_code` split.

## Backend (`HRMS-Backend-v1`) — already implemented on `origin/development`

No new backend code. `MonitoringDeviceAuthorizationController`
(`api/v1/monitoring/device-authorization`) exposes:

| Route | Auth | Body / Query | Behavior |
|---|---|---|---|
| `POST /start` | Anonymous, fingerprint-rate-limited (10/hour) | `{device_name, device_os, device_fingerprint, client_version}` | Creates a `TrayDeviceAuthorization` (Pending, 10 min TTL). Returns `{device_code, user_code, verification_uri, verification_uri_complete, expires_in_seconds, interval_seconds}`. `verification_uri_complete` is server-built from `Urls:AppBaseUrl` config (`https://localhost:4200` in dev) + `/device/activate?request_id=...&user_code=...` — **the frontend route must be registered at exactly `device/activate`** to match. |
| `GET /{requestId}?user_code=` | `[Authorize(TenantPolicy)]` | — | Returns `DeviceAuthorizationPreviewDto` (`device_name`, `device_os`, `client_version`, `expires_at`) for the approval screen. |
| `POST /approve` | `[Authorize(TenantPolicy)]` | `{request_id, user_code}` | Binds the authorization to the calling user/tenant, marks Approved. |
| `POST /token` | Anonymous | `{device_code, device_fingerprint}` | Polls (5s min interval). Returns `TrayAuthResponseDto` on success, or a `code` field (`authorization_pending`/`slow_down`/`expired_token`/`access_denied`) via `ProblemDetails.Extensions["code"]` on failure. |

The only backend work in this pass is switching this repo's active branch onto
`origin/development` (or equivalent) and verifying the feature still builds and its
existing unit tests (`TrayDeviceAuthorizationTests.cs`) still pass — done as part of branch
setup, ahead of the implementation plan below.

## Frontend (`Hrms--Web-application`) — component built, routing/guard work is net-new

`DeviceActivationComponent` (`src/app/modules/device-activation/feature/device-activation/`)
and `DeviceAuthorizationApiService` already exist and already call `preview`/`approve`
correctly. What's missing:

1. **Route registration**: `device/activate` must be added as its own top-level route
   (sibling to the `auth/*` block, not nested under the `MainLayoutComponent`-wrapped
   dashboard children — this is a standalone interstitial, not a dashboard page), with
   `canActivate: [authGuard]`.
2. **`authGuard` return-URL support**: today `authGuard` takes no arguments and
   unconditionally `router.navigate(['/auth/login'])`s with no way back. This is the actual
   crux of "auto-login if already signed in" — the guard's existing `checkSession()` +
   pass-through behavior already gives auto-continue for an existing session; what's
   missing is the round trip for the *not yet* logged-in case. `authGuard` must become
   `async (route, state) => ...` and append `{ queryParams: { returnUrl: state.url } }` when
   redirecting to login.
3. **`LoginComponent` return-URL consumption**: `submit()` currently hardcodes
   `this.router.navigate(['/people/employees'])` on success. It must read
   `returnUrl` from `ActivatedRoute` and navigate there instead, when present, in that same
   branch (the `legalAcceptanceRequired`/`redirectRequired` branches are unaffected).

## TrayApp (`HRMS_TrayApp`) — API client built, IPC/UI wiring is net-new

`OnevoApiClient.StartDeviceAuthorizationAsync`/`PollDeviceAuthorizationAsync` are complete
and correct. Net-new work:

1. **IPC contracts** (`ONEVO.Agent.Shared/IPC/IpcMessages.cs`): `DevicePairingStart` (request),
   `DevicePairingStarted` (correlated reply — verification URI info), `DevicePairingCancel`
   (request), `DevicePairingResult` (**unsolicited push**, not a correlated reply — the
   polling loop that produces it runs long after the original request/response completed).
2. **`NamedPipeClient`**: `SendDevicePairingStartAsync` (correlated, short timeout, mirrors
   `SendActivationAsync`) and `SendDevicePairingCancelAsync` (fire-and-forget, mirrors other
   command sends); a new `OnDevicePairingResult` event wired into `ReadLoopAsync`'s existing
   unsolicited-push branch (same shape as the existing `PolicyPush`/`NotificationPush` cases,
   not the `_pending` correlation dictionary).
3. **`AgentWorker`**: `HandleDevicePairingStartAsync` calls `StartDeviceAuthorizationAsync`,
   replies immediately with the verification URI, then starts a cancellable background
   polling loop (`PollDeviceAuthorizationAsync` every `interval_seconds`, respecting
   `slow_down` and the overall `expires_in_seconds` deadline) that on `Authorized` runs the
   same success tail `HandleActivationCodeSubmitAsync` uses after exchange
   (`PersistAuth` → `SendHeartbeatAsync` → `TryTransition(Stopped)` → `ApplyEnrollmentGates`)
   and pushes the result via `_pipeServer.BroadcastAsync` (the same mechanism
   `PolicySyncService`/`NotificationPollingService` already use for unsolicited pushes) —
   not through the original request's `reply` callback, which is only valid for the initial
   synchronous response. `HandleDevicePairingCancelAsync` cancels the loop.
4. **`ConnectWorkspaceViewModel`/`ConnectWorkspacePage.xaml`**: new `ConnectViaBrowserCommand`
   (replacing the current static `OpenActivationWebsiteCommand`'s body) calls
   `SendDevicePairingStartAsync`, opens the browser at the returned
   `verification_uri_complete` via the existing `Process.Start(UseShellExecute=true)`
   pattern, and sets `IsWaitingForBrowserApproval = true`. Subscribing to
   `OnDevicePairingResult` drives the same success path `VerifyAndConnectAsync` already
   drives (`SessionPreferenceKeys` writes, `Shell.Current.GoToAsync(SetupFlow.AfterActivation)`)
   or surfaces an error. A new Cancel command sends `SendDevicePairingCancelAsync` and resets
   state. The new "waiting" panel must not introduce a `ScrollView` —
   `TrayScreenLayoutContractTests` asserts against that for `ConnectWorkspacePage.xaml`.

`VerifyAndConnectAsync`, `PasteActivationCodeAsync`, and `IsValidActivationCode` are
unchanged.

## Error handling

- **Backend unreachable at `start`**: same "Can't reach the ONEVO backend right now" style
  message already used for the manual flow; no browser opens.
- **User closes the browser tab without deciding**: the authorization simply expires after
  10 minutes; the poll loop's own `expired_token`/deadline handling surfaces "The browser
  request expired — try again."
- **`slow_down`**: not user-visible — the poll loop backs off and continues silently.
- **`access_denied`**: "Request denied in the browser."
- **Poll after Consumed** (e.g. a retried poll racing the success path): the backend's
  `PollDeviceAuthorizationCommandHandler` already requires `Status == Approved` and rejects
  otherwise with `access_denied` — the loop must stop on any terminal result, not just
  `Authorized`.

## Testing

- **Backend**: none new — verify the existing `TrayDeviceAuthorizationTests.cs` and the
  controller build cleanly on the branch this work lands on.
- **Frontend**: `authGuard` returnUrl-append spec; `LoginComponent` returnUrl-consume spec;
  a route spec asserting `device/activate` → `DeviceActivationComponent` with
  `canActivate: [authGuard]`.
- **TrayApp**: `NamedPipeClient`/`FakeNamedPipeClient` tests for the two new send methods
  and the `OnDevicePairingResult` push; `AgentWorker` tests for the new handlers and the
  polling loop's terminal-state handling (`Authorized`/`AccessDenied`/`ExpiredToken`/
  `ServiceUnavailable`, plus `slow_down` backoff), following
  `AgentWorkerLifecycleGateTests`'s construct-and-invoke-internal-handler pattern;
  `ConnectWorkspaceViewModel` tests for the waiting state, cancel, and each terminal
  `OnDevicePairingResult` outcome.

## Unrelated WIP note

`HRMS_TrayApp`'s `bugfix` branch has pre-existing uncommitted changes to `AgentWorker.cs`,
`OnevoApiClient.cs`, and `ConnectWorkspaceViewModel.cs` (department/work-mode/office fields
on the enrollment reply). This work builds on top of that tree as-is. The backend repo's
prior branch (`docs/module-creation-assets-design`) had substantial unrelated uncommitted
work (monitoring feature toggles legal-entity scoping, work-management objective assets);
it was stashed (`git stash`, not discarded) before switching to a new branch
`feature/tray-browser-auto-connect` off `origin/development` for this work.
