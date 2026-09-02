# Tray Monitoring Profile Fix Report

## Outcome

The tray no longer presents an arbitrary company Employee identity. Company context is supplied by the backend-issued device credential, not invented by the tray application.

## Old behavior

- The backend auth payload contained employee display fields but no resolution status.
- The tray cached and displayed any returned employee identity without knowing whether it came from an ambiguous multi-company lookup.

## New behavior

- The backend derives legal-entity context from the authenticated web session during activation/approval and embeds it in the device JWT.
- The tray auth payload mirrors `employee_profile_status` and passes it through service IPC.
- When a legacy device has no unambiguous company context, the tray shows: `Connected — select a company in ONEVO to load your employee profile`.
- The tray sends no `legalEntityId` in activation requests because it has no trusted local source. This intentionally follows the plan's rule not to invent tenant/legal-entity context.
- Effective monitoring policy is obtained using the backend-issued device JWT, whose legal-entity claim selects the correct Employee and company default.

## API/IPC contract

- Auth response adds optional JSON field `employee_profile_status`.
- IPC `EnrollmentResultPayload` adds optional `EmployeeProfileStatus`.
- Existing token and employee display fields remain backward compatible.

## Files changed

- `ONEVO.Agent.Service/Api/OnevoApiClient.cs`
- `ONEVO.Agent.Service/AgentWorker.cs`
- `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`
- `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`

Unrelated pre-existing working-tree changes were preserved.

## Tests and builds

- Tray app tests: 189 passed.
- Focused service API/policy/worker tests: 29 passed.
- Full service test run: 156 passed (rerun with access to the existing `C:\ProgramData\ONEVO\Agent` test credential path).
- Release tray build: passed with zero warnings/errors.
- `git diff --check`: passed (line-ending conversion notices only).

## Remaining risks

- Legacy multi-company devices require reactivation from a selected company before employee details and policy can resolve. This is deliberate and safer than guessing.
- The service suite still depends on write access to its existing `C:\ProgramData\ONEVO\Agent` test path; a future test-only credential-store override would make sandboxed runs more portable.
