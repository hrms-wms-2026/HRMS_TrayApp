# Browser Auto-Connect (Device Pairing) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second, additive way to connect the OneXso WorkPulse desktop tray app —
click "Connect via Browser," approve in a browser tab (auto-continuing if already signed
in, otherwise via a normal login redirect), and the desktop app finishes connecting on its
own, with no code to copy/paste. The existing paste-a-code flow is untouched.

**Architecture:** This is a wiring job, not new protocol design. A complete, unit-tested
RFC 8628 (OAuth Device Authorization Grant) backend already exists
(`MonitoringDeviceAuthorizationController` on the `feature/tray-browser-auto-connect`
branch, based on `origin/development`), and both clients already have matching low-level
scaffolding that nothing currently calls (`OnevoApiClient.StartDeviceAuthorizationAsync`/
`PollDeviceAuthorizationAsync` in TrayApp; `DeviceActivationComponent`/
`DeviceAuthorizationApiService` in the frontend, unrouted). This plan connects those pieces:
a new Angular route + auth-guard return-URL round trip, and new TrayApp IPC/AgentWorker/
ViewModel plumbing around the already-tested HTTP client methods.

**Tech Stack:** .NET 10 / ASP.NET Core (backend, already built), .NET MAUI + a Windows
background service communicating over a named pipe (TrayApp), Angular 21 + Vitest (frontend).

## Global Constraints

- Do not modify `VerifyAndConnectAsync`, `PasteActivationCodeAsync`, or
  `IsValidActivationCode` in `ConnectWorkspaceViewModel.cs` — the manual code-paste flow
  must remain byte-for-byte behaviorally identical.
- No new backend code. The backend feature is already complete and tested; this plan only
  wires the two clients to the endpoints that already exist.
- New TrayApp UI panels must not introduce a `<ScrollView>` anywhere in
  `ConnectWorkspacePage.xaml` — `TrayScreenLayoutContractTests` asserts against this.
- The frontend route for the browser approval screen must be registered at exactly
  `device/activate` (not `connect-device` or `settings/devices`) — the backend
  hard-builds `verification_uri_complete` as `{AppBaseUrl}/device/activate?request_id=...&user_code=...`,
  so any other path silently 404s with no client-side fix possible.
- `HRMS_TrayApp`'s `bugfix` branch has pre-existing unrelated uncommitted changes
  (department/work-mode/office fields on the enrollment reply). Do not revert or discard
  them; this plan's diffs build on top of that tree as-is.

---

## Task 0: Verify the backend branch builds and its device-authorization tests pass

This is a verification-only task — no backend code changes. `HRMS-Backend-v1` is already
on branch `feature/tray-browser-auto-connect` (created from `origin/development`, which
carries the complete `MonitoringDeviceAuthorizationController` feature, including
`TrayDeviceAuthorizationTests.cs`). This task just confirms that branch is in a working
state before building the two clients against it.

**Files:** none (verification only).

- [x] **Step 1: Confirm the branch and a clean working tree**

Run:
```bash
cd /c/onevoNew/HRMS-Backend-v1
git branch --show-current
git status --porcelain=v1
```
Expected: `feature/tray-browser-auto-connect`, and no output from `git status` (clean —
any unrelated WIP from the prior branch was already stashed with message "WIP before
switching to feature/tray-browser-auto-connect...").

- [x] **Step 2: Build the API project**

If a `dotnet run`/`dotnet watch` process is already running against this repo, stop it
first — a live process locks the output DLLs and the build below will fail with
`MSB3027`/`MSB3021` copy errors that are not real compile errors.

Run:
```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Debug
```
Expected: `Build succeeded.`

- [x] **Step 3: Run the device-authorization unit tests**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TrayDeviceAuthorization"
```
Expected: all tests pass (0 failed).

- [x] **Step 4: No commit needed**

This task made no code changes — nothing to commit.

---

## Task 1: Frontend — `authGuard` return-URL support

`authGuard` (`src/app/core/guards/auth.guard.ts`) currently takes no arguments and
redirects to `/auth/login` with no way to return to the original destination. This is the
actual mechanism behind "auto-continue if already logged in, otherwise log in and land
back here" — the guard's existing `checkSession()` pass-through already gives free
auto-continue for an existing session; the missing piece is the round trip for a *not yet*
authenticated visit.

**Files:**
- Modify: `src/app/core/guards/auth.guard.ts`
- Test: `src/app/core/guards/auth.guard.spec.ts` (already exists, already calls the guard
  with two arguments — its assertions need updating, not its call signature)

**Interfaces:**
- Consumes: `AuthStore.checkSession(): Promise<boolean>` (unchanged, existing).
- Produces: `authGuard: CanActivateFn` now reads `route`/`state` and appends
  `returnUrl` as a query param when redirecting — consumed by Task 2's `LoginComponent`.

- [x] **Step 1: Write the failing test**

Replace the second test in `src/app/core/guards/auth.guard.spec.ts` (the redirect case) —
keep the first test (`allows navigation when checkSession resolves true`) unchanged:

```ts
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthStore } from '../auth/state/auth.store';

describe('authGuard', () => {
  function runGuard(url: string) {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url } as never)
    );
  }

  it('allows navigation when checkSession resolves true', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthStore, useValue: { checkSession: () => Promise.resolve(true) } }]
    });

    const result = await TestBed.runInInjectionContext(() => authGuard({} as never, { url: '/dashboard' } as never));

    expect(result).toBe(true);
  });

  it('redirects to /auth/login with a returnUrl and denies navigation when checkSession resolves false', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthStore, useValue: { checkSession: () => Promise.resolve(false) } }]
    });
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate');

    const result = await runGuard('/device/activate?request_id=abc&user_code=XYZ');

    expect(result).toBe(false);
    expect(navigateSpy).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/device/activate?request_id=abc&user_code=XYZ' }
    });
  });
});
```

- [x] **Step 2: Run the test to verify it fails**

Run: `npm test -- auth.guard.spec.ts`
Expected: FAIL — `navigateSpy` was called with `(['/auth/login'])`, not the two-argument
form the new assertion expects.

- [x] **Step 3: Update `authGuard` to append `returnUrl`**

Edit `src/app/core/guards/auth.guard.ts`:

```ts
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from '../auth/state/auth.store';

export const authGuard: CanActivateFn = async (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  const authenticated = await authStore.checkSession();
  if (authenticated) {
    return true;
  }

  router.navigate(['/auth/login'], { queryParams: { returnUrl: state.url } });
  return false;
};
```

- [x] **Step 4: Run the test to verify it passes**

Run: `npm test -- auth.guard.spec.ts`
Expected: PASS (both tests).

- [x] **Step 5: Commit**

```bash
git add src/app/core/guards/auth.guard.ts src/app/core/guards/auth.guard.spec.ts
git commit -m "feat(auth): append returnUrl when authGuard redirects to login"
```

---

## Task 2: Frontend — `LoginComponent` return-URL consumption

`LoginComponent.submit()` currently hardcodes `this.router.navigate(['/people/employees'])`
on a successful login with no legal-consent/redirect requirement. It must read `returnUrl`
from the query string and navigate there instead when present, without disturbing the
`legalAcceptanceRequired`/`redirectRequired` branches.

**Files:**
- Modify: `src/app/core/auth/feature/login/login.component.ts`
- Test: `src/app/core/auth/feature/login/login.component.spec.ts` (create if it doesn't
  already exist — check first with `ls src/app/core/auth/feature/login/`)

**Interfaces:**
- Consumes: `ActivatedRoute.snapshot.queryParamMap` (Angular router, standard).
- Produces: nothing new consumed by later tasks — this is the terminal end of the
  return-URL round trip started in Task 1.

- [x] **Step 1: Check for an existing spec file**

Run: `ls src/app/core/auth/feature/login/`
If `login.component.spec.ts` exists, read it first and add the new test case in its
existing style instead of Step 2's fresh file. If it does not exist, use Step 2 as-is.

- [x] **Step 2: Write the failing test**

Create/extend `src/app/core/auth/feature/login/login.component.spec.ts`:

```ts
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { LoginComponent } from './login.component';
import { AuthStore } from '../../state/auth.store';

describe('LoginComponent', () => {
  function setup(returnUrl: string | null, authStoreOverrides: Partial<{
    login: () => Promise<void>;
    legalAcceptanceRequired: () => boolean;
    redirectRequired: () => boolean;
  }> = {}) {
    TestBed.configureTestingModule({
      imports: [LoginComponent, ReactiveFormsModule],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (key: string) => (key === 'returnUrl' ? returnUrl : null) } }
          }
        },
        {
          provide: AuthStore,
          useValue: {
            login: authStoreOverrides.login ?? (() => Promise.resolve()),
            legalAcceptanceRequired: authStoreOverrides.legalAcceptanceRequired ?? (() => false),
            redirectRequired: authStoreOverrides.redirectRequired ?? (() => false),
            continueUrl: () => null
          }
        }
      ]
    });

    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;
    component.form.setValue({ email: 'user@test.dev', password: 'password123' });
    const router = TestBed.inject(Router);
    return { component, router, navigateSpy: vi.spyOn(router, 'navigate') };
  }

  it('navigates to returnUrl after a successful login when present', async () => {
    const { component, navigateSpy } = setup('/device/activate?request_id=abc&user_code=XYZ');

    await component.submit();

    expect(navigateSpy).toHaveBeenCalledWith(['/device/activate?request_id=abc&user_code=XYZ']);
  });

  it('falls back to /people/employees when no returnUrl is present', async () => {
    const { component, navigateSpy } = setup(null);

    await component.submit();

    expect(navigateSpy).toHaveBeenCalledWith(['/people/employees']);
  });
});
```

- [x] **Step 3: Run the test to verify it fails**

Run: `npm test -- login.component.spec.ts`
Expected: FAIL on the first test — the component currently always navigates to
`/people/employees`, ignoring `returnUrl`.

- [x] **Step 4: Update `LoginComponent`**

Edit `src/app/core/auth/feature/login/login.component.ts`. Add the `ActivatedRoute`
injection and change the final `else` branch of `submit()`:

```ts
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthStore } from '../../state/auth.store';
import { ButtonComponent } from '../../../../shared/ui/button/button.component';
import { CardComponent } from '../../../../shared/ui/card/card.component';
import { buildRootUrl, isTenantHostname } from '../../utils/tenant-redirect';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent],
  templateUrl: './login.component.html'
})
export class LoginComponent implements OnInit {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  store = inject(AuthStore);

  readonly showPassword = signal(false);

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]]
  });

  ngOnInit(): void {
    if (isTenantHostname(window.location.hostname)) {
      window.location.href = buildRootUrl('/auth/login');
    }
  }

  toggleShowPassword(): void {
    this.showPassword.update((v) => !v);
  }

  signInWithGoogle(): void {
    console.log('Initiating Google SSO login...');
  }

  async submit(): Promise<void> {
    if (isTenantHostname(window.location.hostname)) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    try {
      await this.store.login(this.form.getRawValue());
    } catch {
      return;
    }

    if (this.store.legalAcceptanceRequired()) {
      this.router.navigate(['/auth/legal-consent']);
    } else if (this.store.redirectRequired()) {
      const url = this.store.continueUrl();
      if (url) {
        window.location.href = url;
      }
    } else {
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      this.router.navigate([returnUrl ?? '/people/employees']);
    }
  }
}
```

- [x] **Step 5: Run the test to verify it passes**

Run: `npm test -- login.component.spec.ts`
Expected: PASS (both tests).

- [x] **Step 6: Commit**

```bash
git add src/app/core/auth/feature/login/login.component.ts src/app/core/auth/feature/login/login.component.spec.ts
git commit -m "feat(auth): navigate to returnUrl after login when present"
```

---

## Task 3: Frontend — register the `device/activate` route

`DeviceActivationComponent` (`src/app/modules/device-activation/feature/device-activation/`)
and its `DeviceAuthorizationApiService` already exist and already call `preview`/`approve`
correctly (added in commit `2179d23`, never wired into any route). This task registers it.

**Files:**
- Modify: `src/app/app.routes.ts`
- Test: create `src/app/modules/device-activation/device-activation.routes.spec.ts`

**Interfaces:**
- Consumes: `authGuard` (from Task 1, unchanged export name/shape), the existing
  `DeviceActivationComponent` (`src/app/modules/device-activation/feature/device-activation/device-activation.component.ts`,
  no changes needed to that file).
- Produces: the route `device/activate`, which is what `WorkspaceLinks`/AgentWorker's
  `verification_uri_complete` in TrayApp (Task 8) will point the browser at.

- [x] **Step 1: Write the failing test**

Create `src/app/modules/device-activation/device-activation.routes.spec.ts`:

```ts
import { Routes } from '@angular/router';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth.guard';

describe('device/activate route', () => {
  it('registers device/activate as a top-level route guarded by authGuard', () => {
    const deviceActivate = (routes as Routes).find((route) => route.path === 'device/activate');

    expect(deviceActivate).toBeTruthy();
    expect(deviceActivate?.canActivate).toContain(authGuard);
    expect(deviceActivate?.loadComponent).toBeTypeOf('function');
  });

  it('resolves the lazy-loaded component to DeviceActivationComponent', async () => {
    const deviceActivate = (routes as Routes).find((route) => route.path === 'device/activate');
    const loaded = await deviceActivate!.loadComponent!();

    expect((loaded as { name: string }).name).toBe('DeviceActivationComponent');
  });
});
```

- [x] **Step 2: Run the test to verify it fails**

Run: `npm test -- device-activation.routes.spec.ts`
Expected: FAIL — `deviceActivate` is `undefined`, no such route exists yet.

- [x] **Step 3: Register the route**

Edit `src/app/app.routes.ts`. Add a new top-level route entry as a sibling of the `auth`
block (not nested inside the `MainLayoutComponent`-wrapped dashboard children — this is a
standalone interstitial page, not a dashboard page). Insert it directly after the `auth`
route block and before the final `**` wildcard:

```ts
  {
    path: 'device/activate',
    loadComponent: () =>
      import('./modules/device-activation/feature/device-activation/device-activation.component').then(
        (module) => module.DeviceActivationComponent
      ),
    canActivate: [authGuard]
  },
  {
    path: '**',
    redirectTo: 'auth/login',
  },
```

(This replaces just the trailing `{ path: '**', redirectTo: 'auth/login' }` block with the
new route followed by that same wildcard block — the wildcard must remain last.)

- [x] **Step 4: Run the test to verify it passes**

Run: `npm test -- device-activation.routes.spec.ts`
Expected: PASS (both tests).

- [x] **Step 5: Run the full existing route test suite to confirm no regression**

Run: `npm test -- app.routes.spec.ts`
Expected: PASS (unchanged — the new route doesn't touch the `settings`/`attendance`
children this spec asserts on).

- [x] **Step 6: Commit**

```bash
git add src/app/app.routes.ts src/app/modules/device-activation/device-activation.routes.spec.ts
git commit -m "feat(device-activation): register device/activate route behind authGuard"
```

---

## Task 4: TrayApp — IPC message contracts for device pairing

Add the new message types and payloads the TrayApp UI process and the background service
exchange over the named pipe for the browser-pairing flow. `DevicePairingResult` is an
**unsolicited push** (the polling loop that produces it runs long after the original
`DevicePairingStart` request/response completed) — same category as the existing
`PolicyPush`/`NotificationPush`, not a correlated reply.

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- Test: `tests/ONEVO.Agent.Shared.Tests/IpcEnvelopeTests.cs` (extend with a
  serialization round-trip case for the new payloads — read this file first to match its
  existing style before adding to it)

**Interfaces:**
- Produces: `IpcMessageTypes.DevicePairingStart`, `DevicePairingStarted`,
  `DevicePairingCancel`, `DevicePairingResult` (string constants); `DevicePairingStartPayload`
  (request, Tray → Service), `DevicePairingStartedPayload` (correlated reply, Service →
  Tray), `DevicePairingResultPayload` (unsolicited push, Service → Tray) — consumed by
  Task 5 (`NamedPipeClient`) and Task 6 (`AgentWorker`).

- [x] **Step 1: Read the existing shared IPC test file for its style**

Run: `cat tests/ONEVO.Agent.Shared.Tests/IpcEnvelopeTests.cs` (or open it) — match its
existing assertion style (likely serialize-then-deserialize round trips per payload type)
for the new test in Step 2.

- [x] **Step 2: Write the failing test**

Add to `tests/ONEVO.Agent.Shared.Tests/IpcEnvelopeTests.cs` (inside the existing test
class, following its established pattern):

```csharp
[Fact]
public void DevicePairingStartedPayload_RoundTripsThroughJson()
{
    var payload = new DevicePairingStartedPayload(
        VerificationUri: "https://localhost:4200/device/activate",
        VerificationUriComplete: "https://localhost:4200/device/activate?request_id=abc&user_code=XYZ12345",
        ExpiresInSeconds: 600,
        IntervalSeconds: 5);

    var element = JsonSerializer.SerializeToElement(payload);
    var roundTripped = element.Deserialize<DevicePairingStartedPayload>();

    Assert.Equal(payload, roundTripped);
}

[Fact]
public void DevicePairingResultPayload_RoundTripsThroughJson()
{
    var payload = new DevicePairingResultPayload
    {
        Success = true,
        ErrorCode = null,
        EmployeeName = "Test Employee",
        EmployeeEmail = "test.employee@test.dev",
        EmployeeNumber = "EMP-TEST-01"
    };

    var element = JsonSerializer.SerializeToElement(payload);
    var roundTripped = element.Deserialize<DevicePairingResultPayload>();

    Assert.Equal(payload, roundTripped);
}
```

- [x] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter "FullyQualifiedName~DevicePairing"`
Expected: FAIL — `DevicePairingStartedPayload`/`DevicePairingResultPayload` do not exist yet
(compile error).

- [x] **Step 4: Add the message types and payloads**

Edit `ONEVO.Agent.Shared/IPC/IpcMessages.cs`. Add four new constants to
`IpcMessageTypes`, immediately after the `BiometricEnrollmentResult` line (line 64):

```csharp
    /// <summary>Tray → Service: start a browser-based device pairing (RFC 8628 device authorization grant).</summary>
    public const string DevicePairingStart = "DevicePairingStart";

    /// <summary>Service → Tray: correlated reply to DevicePairingStart with the browser URL to open.</summary>
    public const string DevicePairingStarted = "DevicePairingStarted";

    /// <summary>Tray → Service: cancel an in-progress device pairing poll loop.</summary>
    public const string DevicePairingCancel = "DevicePairingCancel";

    /// <summary>Service → Tray: unsolicited push with the terminal outcome of a device pairing (approved, denied, or expired).</summary>
    public const string DevicePairingResult = "DevicePairingResult";
```

Add the payload records at the end of the file, after `BiometricEnrollmentResultPayload`:

```csharp
public sealed record DevicePairingStartPayload(string DeviceName, string DeviceOs, string ClientVersion);

public sealed record DevicePairingStartedPayload(
    bool Success,
    string? ErrorCode,
    string? VerificationUri = null,
    string? VerificationUriComplete = null,
    int ExpiresInSeconds = 0,
    int IntervalSeconds = 0);

/// <summary>Unsolicited push (not a correlated reply) — same shape as EnrollmentResultPayload
/// so the ViewModel drives one shared success/failure path for both connect flows.</summary>
public sealed record DevicePairingResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }   // "ACCESS_DENIED" | "EXPIRED" | "SERVICE_UNAVAILABLE" | "INVALID_STATE"
    public string? EmployeeName { get; init; }
    public string? EmployeeEmail { get; init; }
    public string? EmployeeNumber { get; init; }
    public string? EmployeeProfileStatus { get; init; }
    public string? DepartmentName { get; init; }
    public string? WorkModeLabel { get; init; }
    public string? OfficeName { get; init; }
    public string? OrganizationName { get; init; }
}
```

Note: the Step 2 test above used a positional-style constructor for
`DevicePairingStartedPayload` — this record's declared shape (positional record with
defaults) supports that call exactly as written.

- [x] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter "FullyQualifiedName~DevicePairing"`
Expected: PASS (both tests).

- [x] **Step 6: Commit**

```bash
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs tests/ONEVO.Agent.Shared.Tests/IpcEnvelopeTests.cs
git commit -m "feat(ipc): add DevicePairing message types and payloads"
```

---

## Task 5: TrayApp — `NamedPipeClient` device-pairing send methods and push event

Add `SendDevicePairingStartAsync` (correlated request/response, mirrors
`SendActivationAsync`), `SendDevicePairingCancelAsync` (fire-and-forget, mirrors other
command sends), and a new `OnDevicePairingResult` event wired into `ReadLoopAsync`'s
existing unsolicited-push branch (same pattern as `OnPolicyReceived`/`OnNotificationReceived`).

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/Services/NamedPipeClientDevicePairingTests.cs` (new)

**Interfaces:**
- Consumes: `IpcMessageTypes.DevicePairingStart/Started/Cancel/Result`,
  `DevicePairingStartPayload`/`DevicePairingStartedPayload`/`DevicePairingResultPayload`
  (Task 4).
- Produces: `INamedPipeClient.SendDevicePairingStartAsync(string deviceName, string
  deviceOs, string clientVersion, CancellationToken ct): Task<DevicePairingStartedPayload?>`,
  `INamedPipeClient.SendDevicePairingCancelAsync(CancellationToken ct): Task`,
  `event Action<DevicePairingResultPayload>? OnDevicePairingResult` — consumed by Task 8
  (`ConnectWorkspaceViewModel`).

- [x] **Step 1: Write the failing test**

Since `NamedPipeClient` talks over a real named pipe (no interface seam for a unit test
without standing up an actual pipe server), this task's automated test targets
`FakeNamedPipeClient` — proving it implements the new interface members correctly — and a
narrower `ReadLoopAsync`-shaped assertion isn't practical to unit test in isolation here
(there is no existing precedent for testing `ReadLoopAsync` directly; it's exercised via
integration in `NamedPipeServerBroadcastTests.cs`-style tests against a real pipe, which is
out of scope for this task). Write the fake-side test first:

Create `tests/ONEVO.Agent.TrayApp.Tests/Services/NamedPipeClientDevicePairingTests.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

public class NamedPipeClientDevicePairingTests
{
    [Fact]
    public async Task SendDevicePairingStartAsync_RecordsEnvelope_ReturnsAutoSuccessByDefault()
    {
        var fake = new FakeNamedPipeClient();

        var result = await fake.SendDevicePairingStartAsync("Laptop", "Windows", "1.0.0", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Single(fake.SentEnvelopes, e => e.Type == IpcMessageTypes.DevicePairingStart);
    }

    [Fact]
    public async Task SendDevicePairingStartAsync_ReturnsCannedResult_WhenSet()
    {
        var fake = new FakeNamedPipeClient
        {
            NextDevicePairingStartedResult = new DevicePairingStartedPayload(false, "SERVICE_UNAVAILABLE")
        };

        var result = await fake.SendDevicePairingStartAsync("Laptop", "Windows", "1.0.0", CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public void SimulateDevicePairingResult_InvokesOnDevicePairingResult()
    {
        var fake = new FakeNamedPipeClient();
        DevicePairingResultPayload? received = null;
        fake.OnDevicePairingResult += payload => received = payload;

        var pushed = new DevicePairingResultPayload { Success = true, EmployeeName = "Test Employee" };
        fake.SimulateDevicePairingResult(pushed);

        Assert.Equal(pushed, received);
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~NamedPipeClientDevicePairingTests"`
Expected: FAIL — compile error, `SendDevicePairingStartAsync`/`OnDevicePairingResult`/
`SimulateDevicePairingResult`/`NextDevicePairingStartedResult` don't exist yet.

- [x] **Step 3: Add the new members to `INamedPipeClient`**

Edit `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`. Add the event next to the
existing ones (after `OnNotificationReceived`):

```csharp
    event Action<DevicePairingResultPayload>? OnDevicePairingResult;
```

Add the two new methods after `SendActivationAsync`:

```csharp
    /// <summary>
    /// Starts a browser-based device pairing and waits for the correlated
    /// DevicePairingStarted reply (or timeout) carrying the browser URL to open. The
    /// terminal outcome arrives later, asynchronously, via OnDevicePairingResult.
    /// </summary>
    Task<DevicePairingStartedPayload?> SendDevicePairingStartAsync(
        string deviceName, string deviceOs, string clientVersion, CancellationToken ct);

    /// <summary>Cancels an in-progress device pairing poll loop. Fire-and-forget — no reply expected.</summary>
    Task SendDevicePairingCancelAsync(CancellationToken ct);
```

- [x] **Step 4: Implement the new members in `NamedPipeClient`**

Edit `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`. Add the event next to the existing
ones (after `OnNotificationReceived`, line 27):

```csharp
    public event Action<DevicePairingResultPayload>? OnDevicePairingResult;
```

Add `SendDevicePairingStartAsync` immediately after `SendActivationAsync` (after its
closing brace, before `SendLogoutAsync`), following the exact same
correlate-write-wait-timeout shape:

```csharp
    public async Task<DevicePairingStartedPayload?> SendDevicePairingStartAsync(
        string deviceName, string deviceOs, string clientVersion, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.DevicePairingStart,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new DevicePairingStartPayload(deviceName, deviceOs, clientVersion))
            };
            await WriteEnvelopeAsync(envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            IpcEnvelope reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Device pairing start timed out waiting for DevicePairingStarted");
                return null;
            }

            return reply.Payload?.Deserialize<DevicePairingStartedPayload>();
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    public Task SendDevicePairingCancelAsync(CancellationToken ct) =>
        WriteEnvelopeAsync(new IpcEnvelope { Type = IpcMessageTypes.DevicePairingCancel }, ct);
```

In `ReadLoopAsync`, add `IpcMessageTypes.DevicePairingStarted` to the existing correlated-reply
whitelist (the `or` chain at lines 450–456) — without this, `SendDevicePairingStartAsync`'s
`TaskCompletionSource` never completes and every call hangs to its 15s timeout:

```csharp
                if (!string.IsNullOrEmpty(envelope.CorrelationId)
                    && _pending.TryGetValue(envelope.CorrelationId, out var pending)
                    && envelope.Type is IpcMessageTypes.LifecycleResult
                        or IpcMessageTypes.StatusResponse
                        or IpcMessageTypes.CollectionRecordAck
                        or IpcMessageTypes.EnrollmentResult
                        or IpcMessageTypes.LogoutResult
                        or IpcMessageTypes.BiometricEnrollmentSessionReady
                        or IpcMessageTypes.BiometricEnrollmentResult
                        or IpcMessageTypes.DevicePairingStarted)
                {
                    pending.TrySetResult(envelope);
                }
```

Add a new `case` to the unsolicited-push `switch` (right after the `NotificationPush` case,
line 511, before `CollectionRecordAck`):

```csharp
                    case IpcMessageTypes.DevicePairingResult:
                    {
                        var pairingResult = envelope.Payload?.Deserialize<DevicePairingResultPayload>();
                        if (pairingResult is not null)
                            OnDevicePairingResult?.Invoke(pairingResult);
                        break;
                    }
```

- [x] **Step 5: Extend `FakeNamedPipeClient`**

Edit `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`. Add the event next to
the existing ones:

```csharp
    public event Action<DevicePairingResultPayload>? OnDevicePairingResult;
```

Add the canned-result field and the two methods, following the exact
`NextEnrollmentResult`/`SendActivationAsync` pattern, placed after `CompleteBiometricEnrollmentAsync`:

```csharp
    /// <summary>Optional canned result for SendDevicePairingStartAsync. Null = auto-success.</summary>
    public DevicePairingStartedPayload? NextDevicePairingStartedResult { get; set; }

    public Task<DevicePairingStartedPayload?> SendDevicePairingStartAsync(
        string deviceName, string deviceOs, string clientVersion, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.DevicePairingStart,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new DevicePairingStartPayload(deviceName, deviceOs, clientVersion))
        });

        if (NextDevicePairingStartedResult is not null)
            return Task.FromResult<DevicePairingStartedPayload?>(NextDevicePairingStartedResult);

        return Task.FromResult<DevicePairingStartedPayload?>(
            new DevicePairingStartedPayload(
                true, null,
                "https://localhost:4200/device/activate",
                "https://localhost:4200/device/activate?request_id=fake-request-id&user_code=FAKECODE",
                600, 5));
    }

    public Task SendDevicePairingCancelAsync(CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope { Type = IpcMessageTypes.DevicePairingCancel });
        return Task.CompletedTask;
    }

    public void SimulateDevicePairingResult(DevicePairingResultPayload payload) =>
        OnDevicePairingResult?.Invoke(payload);
```

- [x] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~NamedPipeClientDevicePairingTests"`
Expected: PASS (all three tests).

- [x] **Step 7: Run the full TrayApp test project to confirm no regression**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
Expected: PASS (all tests, including the pre-existing suite — the `FakeNamedPipeClient`
change is purely additive).

- [x] **Step 8: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs tests/ONEVO.Agent.TrayApp.Tests/Services/NamedPipeClientDevicePairingTests.cs
git commit -m "feat(namedpipe): add device pairing start/cancel send methods and result push event"
```

---

## Task 6: TrayApp — `AgentWorker` device-pairing handlers and polling loop

Add the handlers that call the already-tested `OnevoApiClient.StartDeviceAuthorizationAsync`/
`PollDeviceAuthorizationAsync`, and extract the shared "an auth payload just arrived,
finish enrolling" tail out of `HandleActivationCodeSubmitAsync` so both the manual-code
path and the new polling-loop success path use one implementation.

**Files:**
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/AgentWorkerDevicePairingTests.cs` (new)

**Interfaces:**
- Consumes: `OnevoApiClient.StartDeviceAuthorizationAsync(string deviceName, string
  deviceOs, string clientVersion, string deviceFingerprint, CancellationToken ct):
  Task<DeviceAuthorizationStartResult>` and `PollDeviceAuthorizationAsync(string
  deviceCode, string deviceFingerprint, CancellationToken ct):
  Task<DeviceAuthorizationPollResult>` (already implemented, already tested —
  `ONEVO.Agent.Service.Api.OnevoApiClient`); `IpcMessageTypes.DevicePairingStart/Started/Cancel/Result`
  and their payloads (Task 4).
- Produces: `internal Task HandleDevicePairingStartAsync(IpcEnvelope envelope, Func<IpcEnvelope,
  Task> reply)`, `internal Task HandleDevicePairingCancelAsync(IpcEnvelope envelope,
  Func<IpcEnvelope, Task> reply)`, `internal Task PollDevicePairingLoopAsync(
  DeviceAuthorizationStartResult start, string fingerprint, CancellationToken ct,
  Func<TimeSpan, CancellationToken, Task>? delay = null)` (the `delay` seam exists purely
  for test speed — production callers omit it and get real `Task.Delay`) — all `internal`
  so tests can invoke them directly, matching `HandleLifecycleCommandAsync`'s existing
  convention.

- [x] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.Service.Tests/AgentWorkerDevicePairingTests.cs`. This follows
`AgentWorkerLifecycleGateTests`'s construct-a-real-`AgentWorker`-directly pattern, but
supplies a real `OnevoApiClient` wired to a stub `HttpMessageHandler` (the same harness
`OnevoApiClientTests.cs` uses) instead of `null!`, since the device-pairing handlers do
call it:

```csharp
namespace ONEVO.Agent.Service.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

public class AgentWorkerDevicePairingTests
{
    private static AgentWorker BuildWorker(HttpMessageHandler handler)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Unenrolled, out _);

        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — pairing pushes go through the injected reply/loop directly in these tests
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            apiClient,
            new CredentialStore(),
            new DeviceIdentityStore(),
            null!, // EnrollmentCoordinator — not touched by device pairing
            null!, // InactivityEvidenceHandler — not touched by device pairing
            null!  // EvidenceSpoolStore — not touched by device pairing
        );
    }

    private static Task NoDelay(TimeSpan span, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task HandleDevicePairingStartAsync_Success_RepliesWithVerificationUri()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.DeviceAuthorizationStart)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        device_code = "device-secret",
                        user_code = "ABCD2345",
                        verification_uri = "https://localhost:4200/device/activate",
                        verification_uri_complete = "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
                        expires_in_seconds = 600,
                        interval_seconds = 5,
                    })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { code = "authorization_pending" })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingStartedPayload? result = null;
        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.DevicePairingStart,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartPayload("Laptop", "Windows", "1.0.0"))
        };
        await worker.HandleDevicePairingStartAsync(envelope, reply =>
        {
            if (reply.Type == IpcMessageTypes.DevicePairingStarted)
                result = reply.Payload!.Value.Deserialize<DevicePairingStartedPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345", result.VerificationUriComplete);
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_Authorized_EnrollsAndPushesSuccessResult()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.DeviceAuthorizationToken)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        access_token = "eyJ.test",
                        expires_in_seconds = 3600,
                        refresh_token = "raw-refresh",
                        refresh_expires_in_seconds = 7_776_000,
                        employee_name = "Priya Employee",
                        employee_email = "priya@test.dev",
                        employee_number = "EMP-0001",
                    })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent); // heartbeat
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.True(pushed!.Success);
        Assert.Equal("Priya Employee", pushed.EmployeeName);
        Assert.Equal(MonitoringState.Stopped, worker.CurrentStateForTest);
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_AccessDenied_PushesFailureAndStopsPolling()
    {
        var callCount = 0;
        var handler = new StubHandler(request =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { code = "access_denied" })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.False(pushed!.Success);
        Assert.Equal("ACCESS_DENIED", pushed.ErrorCode);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_PendingThenAuthorized_PollsUntilResolved()
    {
        var callCount = 0;
        var handler = new StubHandler(request =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { code = "authorization_pending" })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = "eyJ.test",
                    expires_in_seconds = 3600,
                    refresh_token = "raw-refresh",
                    refresh_expires_in_seconds = 7_776_000,
                })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.True(pushed!.Success);
        Assert.True(callCount >= 3, $"expected at least 3 poll calls (2 pending + 1 authorized + 1 heartbeat), got {callCount}");
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
```

This test references `worker.CurrentStateForTest` and a `pushResult` parameter on
`PollDevicePairingLoopAsync` that don't exist yet — both are added in Step 3 below.

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~AgentWorkerDevicePairingTests"`
Expected: FAIL — compile error (`HandleDevicePairingStartAsync`, `PollDevicePairingLoopAsync`,
`CurrentStateForTest` don't exist).

- [x] **Step 3: Extract the shared enrollment-completion tail**

Edit `ONEVO.Agent.Service/AgentWorker.cs`. Refactor the identity-derivation +
`PersistAuth` + heartbeat + state-transition + gates block that currently lives inline in
`HandleActivationCodeSubmitAsync` (lines 707–737) into a new private helper, and have
`HandleActivationCodeSubmitAsync` call it. Replace lines 707–737 (from
`var (backendDeviceId, tenantId) = ...` through the `ApplyEnrollmentGates();` call) with:

```csharp
        var (completed, completionError) = await CompleteEnrollmentAsync(result.Auth, fingerprint, CancellationToken.None);
        if (!completed)
        {
            await ReplyEnrollmentAsync(envelope, reply, false, completionError, null);
            return;
        }
```

Add the new private helper right after `PersistAuth` (after line 118, before the
`/// <summary>` comment at line 120):

```csharp
    /// <summary>
    /// Shared tail for both connect paths (manual code exchange and browser device
    /// pairing) once a TrayAuthPayload has been obtained: derives/persists device
    /// identity, sends the initial heartbeat, and transitions into Stopped
    /// (enrolled-but-not-clocked-in). Returns false with "INVALID_STATE" only if the
    /// state machine transition itself is rejected (e.g. a race with a concurrent
    /// enrollment) — everything before that point cannot fail once a valid auth
    /// payload is in hand.
    /// </summary>
    private async Task<(bool Success, string? ErrorCode)> CompleteEnrollmentAsync(
        TrayAuthPayload auth, string fingerprint, CancellationToken ct)
    {
        var (backendDeviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(auth.AccessToken);
        var storedIdentity = _deviceIdentityStore.Load();
        var stableDeviceId = storedIdentity?.DeviceId
            ?? backendDeviceId
            ?? Guid.NewGuid().ToString("N");
        var stableAgentId = storedIdentity?.AgentId
            ?? backendDeviceId
            ?? stableDeviceId;
        var identity = new DeviceIdentity
        {
            DeviceId = stableDeviceId,
            AgentId = stableAgentId,
            TenantId = tenantId ?? storedIdentity?.TenantId ?? string.Empty,
            DeviceFingerprint = fingerprint
        };

        PersistAuth(identity, auth);
        await _apiClient.SendHeartbeatAsync(auth.AccessToken, ct);

        if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
            return (false, "INVALID_STATE");

        ApplyEnrollmentGates();
        return (true, null);
    }
```

Add a small internal test-only accessor right after `CompleteEnrollmentAsync` (mirrors
`ApplyEnrollmentGates` already being `internal` for the same reason):

```csharp
    internal MonitoringState CurrentStateForTest => _stateMachine.CurrentState;
```

- [x] **Step 4: Run the existing activation tests to confirm the refactor didn't break anything**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~Activation"`
Expected: PASS — `HandleActivationCodeSubmitAsync`'s observable behavior (replies,
`EnrollmentResultPayload` fields, state transitions) is unchanged by the extraction.

- [x] **Step 5: Add the device-pairing handlers**

In `ONEVO.Agent.Service/AgentWorker.cs`, add two new fields near the top of the class
(after `_evidenceSpool`, line 32) to track the in-flight pairing's cancellation:

```csharp
    private CancellationTokenSource? _pairingCts;
```

Add the two new cases to `HandleMessageAsync`'s switch (after the
`BiometricEnrollmentCaptureFinished` case, before `EvidenceTransferStart`):

```csharp
            case IpcMessageTypes.DevicePairingStart:
                await HandleDevicePairingStartAsync(envelope, reply);
                break;

            case IpcMessageTypes.DevicePairingCancel:
                await HandleDevicePairingCancelAsync(envelope, reply);
                break;
```

Add the handlers themselves, placed after `HandleActivationCodeSubmitAsync`'s closing
brace (after the current line 748, before `ReplyEnrollmentAsync`):

```csharp
    internal async Task HandleDevicePairingStartAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<DevicePairingStartPayload>();
        if (payload is null)
        {
            await reply(BuildDevicePairingStartedEnvelope(envelope.CorrelationId, false, "INVALID_REQUEST"));
            return;
        }

        var fingerprint = DeviceFingerprint.Compute();
        var start = await _apiClient.StartDeviceAuthorizationAsync(
            payload.DeviceName, payload.DeviceOs, payload.ClientVersion, fingerprint, CancellationToken.None);

        if (!start.Success || start.DeviceCode is null)
        {
            _logger.LogWarning("Device pairing start failed. ErrorCode={ErrorCode}", start.ErrorCode);
            await reply(BuildDevicePairingStartedEnvelope(envelope.CorrelationId, false, start.ErrorCode ?? "SERVICE_UNAVAILABLE"));
            return;
        }

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.DevicePairingStarted,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartedPayload(
                true, null, start.VerificationUri, start.VerificationUriComplete,
                start.ExpiresInSeconds, start.IntervalSeconds))
        });

        _pairingCts?.Cancel();
        _pairingCts = new CancellationTokenSource();
        _ = PollDevicePairingLoopAsync(start, fingerprint, _pairingCts.Token);
    }

    private static IpcEnvelope BuildDevicePairingStartedEnvelope(string correlationId, bool success, string? errorCode) =>
        new()
        {
            Type = IpcMessageTypes.DevicePairingStarted,
            CorrelationId = correlationId,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartedPayload(success, errorCode))
        };

    internal Task HandleDevicePairingCancelAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        _pairingCts?.Cancel();
        _pairingCts = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls PollDeviceAuthorizationAsync at the server-specified interval until a terminal
    /// state is reached (Authorized, ExpiredToken, AccessDenied, ServiceUnavailable) or the
    /// authorization's own expiry deadline passes, then pushes exactly one
    /// DevicePairingResult. On Authorized, runs the same completion tail
    /// HandleActivationCodeSubmitAsync uses. <paramref name="delay"/> and
    /// <paramref name="pushResult"/> are test seams — production callers omit both and get
    /// real Task.Delay plus a broadcast over the named pipe.
    /// </summary>
    internal async Task PollDevicePairingLoopAsync(
        DeviceAuthorizationStartResult start,
        string fingerprint,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DevicePairingResultPayload, Task>? pushResult = null)
    {
        delay ??= Task.Delay;
        pushResult ??= PushDevicePairingResultAsync;

        var interval = TimeSpan.FromSeconds(start.IntervalSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(start.ExpiresInSeconds);

        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            await delay(interval, ct);
            if (ct.IsCancellationRequested) return;

            var poll = await _apiClient.PollDeviceAuthorizationAsync(start.DeviceCode!, fingerprint, ct);

            switch (poll.State)
            {
                case DeviceAuthorizationPollState.AuthorizationPending:
                    continue;

                case DeviceAuthorizationPollState.SlowDown:
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case DeviceAuthorizationPollState.Authorized:
                {
                    var (completed, completionError) = await CompleteEnrollmentAsync(poll.Auth!, fingerprint, ct);
                    if (!completed)
                    {
                        await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = completionError });
                        return;
                    }
                    _logger.LogInformation("Device pairing succeeded via browser approval. State={State}", _stateMachine.CurrentState);
                    await pushResult(new DevicePairingResultPayload
                    {
                        Success = true,
                        EmployeeName = poll.Auth!.EmployeeName,
                        EmployeeEmail = poll.Auth.EmployeeEmail,
                        EmployeeNumber = poll.Auth.EmployeeNumber,
                        EmployeeProfileStatus = poll.Auth.EmployeeProfileStatus,
                        DepartmentName = poll.Auth.DepartmentName,
                        WorkModeLabel = poll.Auth.WorkModeLabel,
                        OfficeName = poll.Auth.OfficeName,
                        OrganizationName = poll.Auth.OrganizationName
                    });
                    return;
                }

                case DeviceAuthorizationPollState.ExpiredToken:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "EXPIRED" });
                    return;

                case DeviceAuthorizationPollState.AccessDenied:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "ACCESS_DENIED" });
                    return;

                case DeviceAuthorizationPollState.ServiceUnavailable:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "SERVICE_UNAVAILABLE" });
                    return;
            }
        }

        await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "EXPIRED" });
    }

    private async Task PushDevicePairingResultAsync(DevicePairingResultPayload payload)
    {
        try
        {
            await _pipeServer.BroadcastAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.DevicePairingResult,
                Payload = JsonSerializer.SerializeToElement(payload)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast device pairing result");
        }
    }
```

- [x] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~AgentWorkerDevicePairingTests"`
Expected: PASS (all four tests).

- [x] **Step 7: Run the full service test project to confirm no regression**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj`
Expected: PASS (all tests, including `AgentWorkerLifecycleGateTests` and
`AgentWorkerCollectionSubmitTests`).

- [x] **Step 8: Commit**

```bash
git add ONEVO.Agent.Service/AgentWorker.cs tests/ONEVO.Agent.Service.Tests/AgentWorkerDevicePairingTests.cs
git commit -m "feat(agent): add browser device pairing start/poll/cancel handlers"
```

---

## Task 7: TrayApp — `ConnectWorkspaceViewModel` + `ConnectWorkspacePage.xaml` wiring

Wire the UI: a new `ConnectViaBrowserCommand` opens the browser and shows a waiting panel;
`OnDevicePairingResult` drives the same success path `VerifyAndConnectAsync` already
drives, or shows an error. A Cancel command stops the poll loop. The existing manual-code
UI (`ActivationCode` entry, `VerifyAndConnectCommand`, `PasteActivationCodeCommand`) is
untouched.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelDevicePairingTests.cs` (new — check first whether `ConnectWorkspaceViewModelTests.cs` already exists and extend that instead if so)

**Interfaces:**
- Consumes: `INamedPipeClient.SendDevicePairingStartAsync`/`SendDevicePairingCancelAsync`/
  `OnDevicePairingResult` (Task 5), `WorkspaceLinks` (unchanged), `SetupFlow.AfterActivation`
  (unchanged), `SessionPreferenceKeys` (unchanged).
- Produces: nothing consumed by later tasks — this is the last task.

- [x] **Step 1: Check for an existing ViewModel test file**

Run: `ls tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ | grep -i ConnectWorkspace`
If `ConnectWorkspaceViewModelTests.cs` exists, read it for its construction pattern
(likely `new ConnectWorkspaceViewModel(fakePipe, fakePreferences)`) and add the new tests
into that file instead of creating a new one. If it doesn't exist, use the file path above.

- [x] **Step 2: Write the failing test**

```csharp
namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

public class ConnectWorkspaceViewModelDevicePairingTests
{
    [Fact]
    public async Task ConnectViaBrowserCommand_Success_SetsWaitingState()
    {
        var pipe = new FakeNamedPipeClient();
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);

        await vm.ConnectViaBrowserCommand.ExecuteAsync(null);

        Assert.True(vm.IsWaitingForBrowserApproval);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ConnectViaBrowserCommand_StartFailure_SetsErrorAndDoesNotWait()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextDevicePairingStartedResult = new DevicePairingStartedPayload(false, "SERVICE_UNAVAILABLE")
        };
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);

        await vm.ConnectViaBrowserCommand.ExecuteAsync(null);

        Assert.False(vm.IsWaitingForBrowserApproval);
        Assert.Equal("Can't reach the ONEVO backend right now. Check your connection and try again.", vm.ErrorMessage);
    }

    [Fact]
    public async Task OnDevicePairingResult_Success_UpdatesConnectionStateLikeManualConnect()
    {
        var pipe = new FakeNamedPipeClient();
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);
        await vm.ConnectViaBrowserCommand.ExecuteAsync(null);

        pipe.SimulateDevicePairingResult(new DevicePairingResultPayload
        {
            Success = true,
            EmployeeName = "Priya Employee",
            EmployeeNumber = "EMP-0001"
        });

        Assert.False(vm.IsWaitingForBrowserApproval);
        Assert.True(vm.IsConnected);
        Assert.Contains("EMP-0001", vm.ConnectionLabel);
    }

    [Fact]
    public async Task OnDevicePairingResult_AccessDenied_SetsErrorAndStopsWaiting()
    {
        var pipe = new FakeNamedPipeClient();
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);
        await vm.ConnectViaBrowserCommand.ExecuteAsync(null);

        pipe.SimulateDevicePairingResult(new DevicePairingResultPayload { Success = false, ErrorCode = "ACCESS_DENIED" });

        Assert.False(vm.IsWaitingForBrowserApproval);
        Assert.False(vm.IsConnected);
        Assert.Equal("Request denied in the browser.", vm.ErrorMessage);
    }

    [Fact]
    public async Task CancelBrowserApprovalCommand_SendsCancelAndResetsWaitingState()
    {
        var pipe = new FakeNamedPipeClient();
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);
        await vm.ConnectViaBrowserCommand.ExecuteAsync(null);

        await vm.CancelBrowserApprovalCommand.ExecuteAsync(null);

        Assert.False(vm.IsWaitingForBrowserApproval);
        Assert.Single(pipe.SentEnvelopes, e => e.Type == IpcMessageTypes.DevicePairingCancel);
    }
}
```

If `FakePreferencesStore` doesn't already exist under `tests/ONEVO.Agent.TrayApp.Tests/Fakes/`,
grep for how `VerifyAndConnectAsync`'s existing tests construct an `IPreferencesStore` fake
(`grep -rn "IPreferencesStore" tests/ONEVO.Agent.TrayApp.Tests/`) and reuse that exact fake
instead of inventing a new one.

- [x] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~ConnectWorkspaceViewModelDevicePairingTests"`
Expected: FAIL — compile error, `ConnectViaBrowserCommand`/`IsWaitingForBrowserApproval`/
`CancelBrowserApprovalCommand` don't exist yet.

- [x] **Step 4: Update `ConnectWorkspaceViewModel`**

Edit `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`. Add two new
observable properties after `_hintText` (line 21):

```csharp
    [ObservableProperty] private bool _isWaitingForBrowserApproval;
```

Subscribe to the new push event in the constructor, right after the existing
`_pipe.OnStateReceived += ...` block (after line 59, before the closing `}` of the
constructor):

```csharp
        _pipe.OnDevicePairingResult += payload =>
        {
            try { MainThread.BeginInvokeOnMainThread(() => HandleDevicePairingResult(payload)); }
            catch { HandleDevicePairingResult(payload); }
        };
```

Replace the existing `OpenActivationWebsite` command (lines 138–153) with the new async
browser-pairing command, its cancel counterpart, and the shared result handler:

```csharp
    [RelayCommand]
    private async Task ConnectViaBrowserAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        var started = await _pipe.SendDevicePairingStartAsync(
            Environment.MachineName, "Windows", VersionText, ct);

        if (started is null)
        {
            ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
            return;
        }

        if (!started.Success)
        {
            ErrorMessage = started.ErrorCode switch
            {
                "SERVICE_UNAVAILABLE" => "Can't reach the ONEVO backend right now. Check your connection and try again.",
                _ => started.ErrorCode ?? "Could not start browser connect."
            };
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = started.VerificationUriComplete,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if the browser cannot open (unit tests / restricted hosts) — the
            // waiting panel + Cancel button still let the user retry or back out.
        }

        IsWaitingForBrowserApproval = true;
    }

    [RelayCommand]
    private async Task CancelBrowserApprovalAsync(CancellationToken ct)
    {
        await _pipe.SendDevicePairingCancelAsync(ct);
        IsWaitingForBrowserApproval = false;
    }

    private void HandleDevicePairingResult(DevicePairingResultPayload result)
    {
        IsWaitingForBrowserApproval = false;

        if (!result.Success)
        {
            ErrorMessage = result.ErrorCode switch
            {
                "ACCESS_DENIED" => "Request denied in the browser.",
                "EXPIRED" => "The browser request expired — try again.",
                "SERVICE_UNAVAILABLE" => "Can't reach the ONEVO backend right now. Check your connection and try again.",
                _ => result.ErrorCode ?? "Browser connect failed."
            };
            IsConnected = false;
            ConnectionLabel = "Not Connected";
            return;
        }

        SessionPreferenceKeys.ClearAll(_preferences);
        if (!string.IsNullOrWhiteSpace(result.EmployeeName))
            _preferences.Set(SessionPreferenceKeys.EmployeeDisplayName, result.EmployeeName);
        if (!string.IsNullOrWhiteSpace(result.EmployeeEmail))
            _preferences.Set(SessionPreferenceKeys.EmployeeEmail, result.EmployeeEmail);
        if (!string.IsNullOrWhiteSpace(result.EmployeeNumber))
            _preferences.Set(SessionPreferenceKeys.EmployeeId, result.EmployeeNumber);
        if (!string.IsNullOrWhiteSpace(result.DepartmentName))
            _preferences.Set(SessionPreferenceKeys.Department, result.DepartmentName);
        if (!string.IsNullOrWhiteSpace(result.WorkModeLabel))
            _preferences.Set(SessionPreferenceKeys.WorkMode, result.WorkModeLabel);
        if (!string.IsNullOrWhiteSpace(result.OfficeName))
            _preferences.Set(SessionPreferenceKeys.OfficeName, result.OfficeName);
        if (!string.IsNullOrWhiteSpace(result.OrganizationName))
            _preferences.Set(SessionPreferenceKeys.Organization, result.OrganizationName);
        _preferences.Set(SessionPreferenceKeys.DeviceName, Environment.MachineName);

        IsConnected = true;
        ConnectionLabel = result.EmployeeProfileStatus == "company_context_required"
            ? "Connected — select a company in ONEVO to load your employee profile"
            : BuildConnectedLabel(result.EmployeeNumber, result.EmployeeName);
        try { Shell.Current.GoToAsync(SetupFlow.AfterActivation); }
        catch { /* unit tests */ }
    }
```

`WorkspaceLinks` is no longer referenced by this file once `OpenActivationWebsite` is
replaced — leave `WorkspaceLinks.cs` itself untouched (Task 3's frontend route lives at
the tenant-agnostic base host the backend already builds into `verification_uri_complete`,
not at `WorkspaceLinks.PortalUrl`).

- [x] **Step 5: Update the XAML**

Edit `ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml`. Replace the "Open Activation
Website" `Border` block (lines 156–172) with two mutually-exclusive blocks — the original
button (now `IsVisible`-gated to hide while waiting) and a new compact waiting panel with
a Cancel button, using the same `TraySecondaryActionBorder`/`TrayCompactGlassCard` styles
already used elsewhere on this page so no new style resources are needed, and no
`ScrollView` is introduced:

```xml
          <Border Style="{StaticResource TraySecondaryActionBorder}" Margin="0,0,0,16"
                  IsVisible="{Binding IsWaitingForBrowserApproval, Converter={StaticResource InvertBoolConverter}}">
            <Grid>
              <Button Command="{Binding ConnectViaBrowserCommand}"
                      Style="{StaticResource TrayPrimaryActionOverlay}"
                      Text=""
                      SemanticProperties.Description="Connect via browser" />
              <HorizontalStackLayout InputTransparent="True"
                                     HorizontalOptions="Center"
                                     VerticalOptions="Center"
                                     Spacing="8">
                <Label Text="{StaticResource IconGlobe}" FontFamily="Segoe MDL2 Assets"
                       FontSize="16" TextColor="{StaticResource Primary}" VerticalOptions="Center" />
                <Label Text="Connect via Browser" FontSize="15" FontAttributes="Bold"
                       TextColor="{StaticResource Primary}" VerticalOptions="Center" />
              </HorizontalStackLayout>
            </Grid>
          </Border>

          <Border Style="{StaticResource TrayCompactGlassCard}" Padding="14,12" Margin="0,0,0,16"
                  IsVisible="{Binding IsWaitingForBrowserApproval}">
            <VerticalStackLayout Spacing="8">
              <HorizontalStackLayout Spacing="8">
                <ActivityIndicator IsRunning="{Binding IsWaitingForBrowserApproval}"
                                   Color="{StaticResource Primary}"
                                   WidthRequest="18" HeightRequest="18" />
                <Label Text="Waiting for approval in your browser…" FontSize="13" FontAttributes="Bold"
                       TextColor="{StaticResource TextPrimary}" VerticalOptions="Center" />
              </HorizontalStackLayout>
              <Button Command="{Binding CancelBrowserApprovalCommand}"
                      Text="Cancel"
                      Style="{StaticResource TrayPrimaryActionOverlay}"
                      BackgroundColor="Transparent"
                      TextColor="{StaticResource TextSecondary}"
                      FontSize="12"
                      HeightRequest="32"
                      HorizontalOptions="Start" />
            </VerticalStackLayout>
          </Border>
```

- [x] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~ConnectWorkspaceViewModelDevicePairingTests"`
Expected: PASS (all five tests).

- [x] **Step 7: Run the layout contract test to confirm no ScrollView was introduced**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~TrayScreenLayoutContractTests"`
Expected: PASS.

- [x] **Step 8: Run the full TrayApp UI test project to confirm no regression**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
Expected: PASS (all tests, including any pre-existing `ConnectWorkspaceViewModel` tests for
the manual-code path — those must be unaffected since `VerifyAndConnectAsync`/
`PasteActivationCodeAsync`/`IsValidActivationCode` were not touched).

- [ ] **Step 9: Manual smoke check (both processes)**

This step cannot be automated in this plan — it requires a running backend, a browser, and
a signed-in test tenant user. Note it for whoever executes this plan, but do not block on
it to consider the plan complete:

```bash
dotnet run --project ONEVO.Agent.Service/ONEVO.Agent.Service.csproj -c Debug
dotnet run --project ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0
```
Click "Connect via Browser," confirm the browser opens to `/device/activate?request_id=...&user_code=...`,
sign in (or confirm auto-continue if already signed in), click Accept, and confirm the
TrayApp transitions to the connected/review screen within a few seconds without any manual
step in the app itself.

- [x] **Step 10: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelDevicePairingTests.cs
git commit -m "feat(connect): add Connect via Browser flow alongside manual code entry"
```

---

## Self-Review

**Spec coverage:**
- Backend already implemented, verified in Task 0. ✓
- `authGuard` return-URL round trip (the actual "auto-login if already signed in, else
  redirect back" mechanism) — Task 1. ✓
- `LoginComponent` returnUrl consumption — Task 2. ✓
- `device/activate` route registration matching the backend's hardcoded
  `verification_uri_complete` path — Task 3. ✓
- IPC contracts (`DevicePairingStart/Started/Cancel/Result`) — Task 4. ✓
- `NamedPipeClient` send methods + unsolicited push event, with the critical
  `_pending` whitelist addition (Task 5 Step 4) so `SendDevicePairingStartAsync` doesn't
  hang to timeout — Task 5. ✓
- `AgentWorker` handlers, polling loop with `slow_down`/`expired_token`/`access_denied`
  handling, and reuse of the existing enrollment-completion logic via the extracted
  `CompleteEnrollmentAsync` helper — Task 6. ✓
- `ConnectWorkspaceViewModel`/XAML wiring, waiting state, Cancel, and the layout-contract
  constraint (no `ScrollView`) — Task 7. ✓
- Manual code-paste flow left untouched — verified unmodified in every task that touches
  the same files (Tasks 6, 7) and re-confirmed via full test-suite runs. ✓

**Type consistency:** `DevicePairingStartedPayload`, `DevicePairingResultPayload`, and
`DevicePairingStartPayload` (Task 4) are used with identical shapes across Tasks 5, 6, and
7 — checked field-by-field against Task 4's declarations while writing each task.
`DeviceAuthorizationStartResult`/`DeviceAuthorizationPollResult`/`DeviceAuthorizationPollState`
(Task 6) are the pre-existing, already-tested types from `OnevoApiClient.cs` — not
redefined anywhere in this plan.

**No placeholders:** every step above contains complete code, not a description of code to
write.
