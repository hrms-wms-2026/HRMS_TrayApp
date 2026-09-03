# Tray Clock-In Policy Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate the tray app's Clock In/Out UI on the employee's `ClockInPolicy` work-mode eligibility (`AllowedClockInMethods.DesktopTray`), and make the tray's clock-in/out action a real, policy-enforced backend attendance write instead of a local-only state toggle — with a single poll-driven state-observation mechanism used by every employee regardless of eligibility.

**Architecture:** Backend exposes eligibility via the existing tray-policy endpoint and adds two new tray-authenticated commands (clock-in/clock-out) plus a lightweight status query, all reusing the existing `AttendanceTodayStateService`/`ClockInCommandHandler`/`ClockOutCommandHandler` logic via an identity-parameterized overload rather than duplicating it. The Agent Service gets a new 60-second poller (modeled on the existing `NotificationPollingService`) that reconciles local `MonitoringState` with backend truth for everyone; the existing local Clock In/Out button becomes one way to *cause* that backend state to change, not a separate way to *observe* it. The tray app hides the Clock In/Out UI when ineligible and routes to a new minimal landing page instead.

**Tech Stack:** ASP.NET Core / MediatR / EF Core (`HRMS-Backend-v1`), .NET MAUI + `BackgroundService` (`HRMS_TrayApp`), xUnit for both.

## Global Constraints

- Fail closed everywhere: any resolution failure or missing policy means `TrayClockInEnabled = false` and no forced state change — matches `PolicyCache.CreateDefault()` and `LifecycleGate`'s existing philosophy.
- Break start/end is out of scope — untouched by every task in this plan.
- `AllowedClockInMethods.DesktopTray` gates Clock **In** only, mirroring the real `ClockOutCommandHandler` (which has no method gate at all today — clock-out isn't source-restricted for `web` either).
- Every new backend endpoint lives under `[Authorize(Policy = "TrayDevicePolicy")]`, resolving identity via `ITrayCurrentDevice` — never `ICurrentUser`, since `ICurrentUser.UserId` reads a claim that holds the *device's* id on a Device JWT, not the employee's.
- The new 60s poll runs for every enrolled employee, not conditionally on `TrayClockInEnabled` — see spec `docs/superpowers/specs/2026-09-03-tray-clock-in-policy-gating-design.md` for why (an employee can have both `Web` and `DesktopTray` enabled at once).

---

## Task 1: `AttendanceTodayStateService` — identity-parameterized context resolution

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/TimeAttendance/Services/IAttendanceTodayStateService.cs`
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/TimeAttendance/Services/AttendanceTodayStateServiceTests.cs`

**Interfaces:**
- Produces: `IAttendanceTodayStateService.ResolveContextAsync(Guid tenantId, Guid userId, CancellationToken ct = default)` returning `Task<Result<AttendanceTodayContext>>` — used by Tasks 2, 3, 4, 5.

- [ ] **Step 1: Read the existing test file to find the test fixture/mock setup pattern**

Open `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/TimeAttendance/Services/AttendanceTodayStateServiceTests.cs` (create the directory/file if it doesn't exist yet) and note how `ICurrentUser`, `IEmployeeRepository`, `ILegalEntityRepository` etc. are mocked for the existing `ResolveContextAsync()` tests — the new overload's test reuses the same mocks, just calling the new method signature directly instead of going through `ICurrentUser`.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task ResolveContextAsync_WithExplicitIdentity_IgnoresCurrentUser()
{
    // Arrange: mock currentUser.TenantId/UserId to a DIFFERENT guid than the one passed in,
    // so a passing test proves the explicit-identity overload doesn't fall back to ICurrentUser.
    var explicitTenantId = Guid.NewGuid();
    var explicitUserId = Guid.NewGuid();
    var currentUserTenantId = Guid.NewGuid(); // deliberately different
    var currentUserUserId = Guid.NewGuid();   // deliberately different

    var currentUser = new Mock<ICurrentUser>();
    currentUser.Setup(c => c.IsAuthenticated).Returns(true);
    currentUser.Setup(c => c.TenantId).Returns(currentUserTenantId);
    currentUser.Setup(c => c.UserId).Returns(currentUserUserId);

    var employees = new Mock<IEmployeeRepository>();
    var expectedEmployee = new Employee { Id = explicitUserId, TenantId = explicitTenantId, LegalEntityId = Guid.NewGuid() };
    employees.Setup(e => e.GetDefaultForUserAsync(explicitTenantId, explicitUserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(expectedEmployee);
    // employees.GetDefaultForUserAsync for the currentUser ids is NOT set up — if the
    // implementation wrongly falls back to ICurrentUser, Moq's strict-enough default
    // (returns null for an unconfigured call) makes ResolveContextAsync return NotFound,
    // and the assertion below fails.

    var sut = CreateSut(currentUser.Object, employees.Object /*, ...other mocks as the existing suite already wires them */);

    var result = await sut.ResolveContextAsync(explicitTenantId, explicitUserId, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(explicitEmployee.Id, result.Value!.Employee.Id);
}
```

Wire `CreateSut` to whatever helper the existing test file already uses to construct `AttendanceTodayStateService` — reuse it, adding the two new explicit parameters only where `ResolveContextAsync` is called.

- [ ] **Step 3: Run test to verify it fails**

Run (from `HRMS-Backend-v1`): `dotnet test tests/ONEVO.Tests.Unit --filter "ResolveContextAsync_WithExplicitIdentity_IgnoresCurrentUser"`
Expected: FAIL to compile — `ResolveContextAsync(Guid, Guid, CancellationToken)` doesn't exist yet.

- [ ] **Step 4: Add the overload**

In `IAttendanceTodayStateService.cs`, add to the interface:

```csharp
Task<Result<AttendanceTodayContext>> ResolveContextAsync(
    Guid tenantId, Guid userId, CancellationToken ct = default);
```

In `AttendanceTodayStateService.cs`, replace the existing `ResolveContextAsync(CancellationToken ct = default)` method body with a delegation, and add the new explicit-identity overload containing the body that was there before:

```csharp
public Task<Result<AttendanceTodayContext>> ResolveContextAsync(CancellationToken ct = default)
{
    if (!currentUser.IsAuthenticated)
        return Task.FromResult(Result<AttendanceTodayContext>.Forbidden());

    return ResolveContextAsync(currentUser.TenantId, currentUser.UserId, ct);
}

public async Task<Result<AttendanceTodayContext>> ResolveContextAsync(
    Guid tenantId, Guid userId, CancellationToken ct = default)
{
    if (tenantId == Guid.Empty)
        return Result<AttendanceTodayContext>.Forbidden("Tenant context missing.");

    var employee = await employees.GetDefaultForUserAsync(tenantId, userId, ct);
    if (employee?.LegalEntityId is null)
        return Result<AttendanceTodayContext>.NotFound("Current employee record was not found.");

    var legalEntity = await legalEntities.GetByIdForTenantAsync(
        tenantId, employee.LegalEntityId.Value, ct);
    if (legalEntity is null)
        return Result<AttendanceTodayContext>.NotFound("Legal entity was not found.");

    var utcNow = dateTime.UtcNow;
    var scheduleResolution = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
    var timezone = scheduleResolution.Timezone;
    var zone = scheduleResolution.TimeZone;
    var workDate = scheduleResolution.WorkDate;
    var localNow = scheduleResolution.LocalNow;
    var schedule = scheduleResolution.Schedule;

    var expectedAreaResult = await expectedWorkAreas.ResolveAsync(employee, legalEntity, workDate, ct);
    if (!expectedAreaResult.IsSuccess || expectedAreaResult.Value is null)
        return Result<AttendanceTodayContext>.Failure(
            expectedAreaResult.Error ?? "The expected work area could not be resolved.",
            expectedAreaResult.StatusCode ?? 409);

    var expectedArea = expectedAreaResult.Value;
    var policy = await ResolvePolicyAsync(legalEntity.Id, workDate, NormalizeWorkMode(expectedArea.WorkArea), ct);

    return Result<AttendanceTodayContext>.Success(new AttendanceTodayContext(
        employee,
        legalEntity,
        timezone,
        zone,
        workDate,
        utcNow,
        localNow,
        schedule,
        expectedArea.WorkArea,
        expectedArea.Source,
        policy.Policy,
        policy.Status,
        policy.AllowedMethods,
        GetLocalDayWindow(workDate, zone)));
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "ResolveContextAsync_WithExplicitIdentity_IgnoresCurrentUser"`
Expected: PASS

- [ ] **Step 6: Run the full existing test suite for this file to confirm no regression**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AttendanceTodayStateServiceTests"`
Expected: All PASS — the parameterless overload's existing tests must still pass unchanged, since it now just delegates.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/Services/IAttendanceTodayStateService.cs src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/Services/AttendanceTodayStateServiceTests.cs
git commit -m "feat(attendance): add identity-parameterized ResolveContextAsync overload"
```

---

## Task 2: Expose `TrayClockInEnabled` on the tray policy response

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs`
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/Policy/Queries/GetEffectiveTrayPolicy/GetEffectiveTrayPolicyQueryHandler.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/Monitoring/Policy/GetEffectiveTrayPolicyQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IAttendanceTodayStateService.ResolveContextAsync(Guid, Guid, CancellationToken)` from Task 1.
- Produces: `TrayAgentPolicyDto.TrayClockInEnabled` (bool), JSON key `tray_clock_in_enabled`.

- [ ] **Step 1: Write the failing test**

Add to `GetEffectiveTrayPolicyQueryHandlerTests.cs` (mirror the existing test class's mock-setup helper):

```csharp
[Fact]
public async Task Handle_EmployeeWorkModeHasDesktopTrayEnabled_ReturnsTrayClockInEnabledTrue()
{
    var todayState = new Mock<IAttendanceTodayStateService>();
    todayState.Setup(t => t.ResolveContextAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<AttendanceTodayContext>.Success(BuildContext(
            allowedMethods: new AllowedClockInMethods(Web: true, DesktopTray: true, Biometric: false, PhotoRequired: false, LocationRequired: false, AllowedRadiusMeters: null))));

    var sut = CreateSut(todayState: todayState.Object /*, existing mocks unchanged */);

    var result = await sut.Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.True(result.Value!.TrayClockInEnabled);
}

[Fact]
public async Task Handle_TodayStateResolutionFails_ReturnsTrayClockInEnabledFalse()
{
    var todayState = new Mock<IAttendanceTodayStateService>();
    todayState.Setup(t => t.ResolveContextAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("no employee"));

    var sut = CreateSut(todayState: todayState.Object);

    var result = await sut.Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

    Assert.True(result.IsSuccess); // the policy response itself still succeeds — monitoring toggles are unaffected
    Assert.False(result.Value!.TrayClockInEnabled);
}
```

Use whatever `BuildContext(...)`/`CreateSut(...)` helpers the existing test file already has; add an `IAttendanceTodayStateService` parameter to `CreateSut` if it doesn't accept one yet, defaulting to a Mock that returns a successful context with `DesktopTray: false` so every other existing test in this file keeps passing unmodified.

- [ ] **Step 2: Run test to verify it fails**

Run (from `HRMS-Backend-v1`): `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEffectiveTrayPolicyQueryHandlerTests"`
Expected: FAIL to compile — `TrayClockInEnabled` doesn't exist on `TrayAgentPolicyDto`, constructor doesn't accept `IAttendanceTodayStateService`.

- [ ] **Step 3: Add the field to the DTO**

In `TrayAgentPolicyDto.cs`, add a new trailing parameter with a default (positional record — must go after the two existing defaulted params):

```csharp
public sealed record TrayAgentPolicyDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("effective_scope")] string EffectiveScope = "employee",
    [property: JsonPropertyName("location_tracking_enabled")] bool LocationTrackingEnabled = false,
    [property: JsonPropertyName("tray_clock_in_enabled")] bool TrayClockInEnabled = false);
```

- [ ] **Step 4: Wire the handler**

In `GetEffectiveTrayPolicyQueryHandler.cs`, add `IAttendanceTodayStateService todayState` to the constructor (alongside the existing `_device`, `_tenants`, etc. — follow the existing constructor-parameter-to-field pattern in this class), then in `Handle`, after the existing toggle resolution and before constructing the return value:

```csharp
var todayContextResult = await todayState.ResolveContextAsync(tenantId, employeeId, cancellationToken);
var trayClockInEnabled = todayContextResult.IsSuccess
    && todayContextResult.Value!.AllowedClockInMethods.DesktopTray;
```

Update the `TrayAgentPolicyDto` construction to pass `TrayClockInEnabled: trayClockInEnabled` as a named argument, and fold it into `ComputeVersion`'s fingerprint by adding a `bool trayClockInEnabled` parameter to that method and appending `:{trayClockInEnabled}` to the fingerprint string (update both the method signature and its one call site in `Handle`).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEffectiveTrayPolicyQueryHandlerTests"`
Expected: All PASS.

- [ ] **Step 6: Register the new constructor dependency in DI (if not auto-resolved)**

Check `HRMS-Backend-v1/src/ONEVO.Infrastructure/DependencyInjection.cs` for how `GetEffectiveTrayPolicyQueryHandler` and `IAttendanceTodayStateService` are registered — MediatR handlers are typically auto-registered via assembly scanning, and `IAttendanceTodayStateService` is already registered (it's used by `ClockInCommandHandler` today). If both are already scanned/registered, no DI change is needed; if `IAttendanceTodayStateService`'s registration lifetime would create a captive-dependency warning with the handler's lifetime, match whatever lifetime the existing `ClockInCommandHandler` registration uses.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs src/ONEVO.Application/Features/Monitoring/Policy/Queries/GetEffectiveTrayPolicy/GetEffectiveTrayPolicyQueryHandler.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Policy/GetEffectiveTrayPolicyQueryHandlerTests.cs
git commit -m "feat(monitoring): expose TrayClockInEnabled on the tray policy response"
```

---

## Task 3: Refactor `ClockInCommandHandler` to expose a reusable internal entry point

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommandHandler.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/TimeAttendance/Commands/ClockInCommandHandlerTests.cs`

**Interfaces:**
- Produces: `ClockInCommandHandler.HandleForContextAsync(AttendanceTodayContext context, string source, CancellationToken ct)` returning `Task<Result<AttendanceTodayResponse>>` (internal, `[assembly: InternalsVisibleTo]` already covers `ONEVO.Tests.Unit` if the existing tests reference internals — otherwise make it `public`) — used by Task 4's new `TrayClockInCommandHandler`.

This is a pure refactor — no behavior change for the existing web `ClockInCommand` path. Its purpose is letting the new tray command handler (Task 4) reuse the exact same mutation logic without duplicating it.

- [ ] **Step 1: Run the existing test suite first to establish a green baseline**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ClockInCommandHandlerTests"`
Expected: All PASS (baseline before refactor).

- [ ] **Step 2: Extract the post-context-resolution logic into a new internal method**

In `ClockInCommandHandler.cs`, change:

```csharp
public async Task<Result<AttendanceTodayResponse>> Handle(
    ClockInCommand request, CancellationToken ct)
{
    var contextResult = await todayState.ResolveContextAsync(ct);
    if (!contextResult.IsSuccess)
        return ToTodayFailure(contextResult);

    var context = contextResult.Value!;
    if (context.Schedule.Status != "configured")
        return Result<AttendanceTodayResponse>.Conflict("schedule_not_configured");

    if (context.PolicyStatus == "not_configured")
        return Result<AttendanceTodayResponse>.Conflict("clock_in_policy_not_configured");

    if (context.PolicyStatus == "configuration_conflict")
        return Result<AttendanceTodayResponse>.Conflict("multiple_active_company_policies");

    if (!context.AllowedClockInMethods.Web)
        return Result<AttendanceTodayResponse>.Forbidden(
            "Clock-in source web is not allowed by the active policy.");

    var source = request.Source.Trim().ToLowerInvariant();
    try
    {
        var mutation = await unitOfWork.ExecuteInTransactionAsync(
            async transactionCt => await MutateAsync(context, source, transactionCt), ct);

        if (!mutation.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                mutation.Error!, mutation.StatusCode ?? 400);
    }
    catch (UniqueConstraintConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just created by another request. Please refresh and try again.");
    }
    catch (ConcurrencyConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just updated by another request. Please refresh and try again.");
    }

    return await todayState.GetTodayAsync(ct);
}
```

into two methods — the public `Handle` now only resolves context via the existing parameterless call and delegates everything else, and the new `HandleForContextAsync` takes an already-resolved context (so a caller with a different identity source, like the tray command, can supply its own):

```csharp
public async Task<Result<AttendanceTodayResponse>> Handle(
    ClockInCommand request, CancellationToken ct)
{
    var contextResult = await todayState.ResolveContextAsync(ct);
    if (!contextResult.IsSuccess)
        return ToTodayFailure(contextResult);

    return await HandleForContextAsync(contextResult.Value!, request.Source, ct);
}

public async Task<Result<AttendanceTodayResponse>> HandleForContextAsync(
    AttendanceTodayContext context, string sourceRaw, CancellationToken ct)
{
    if (context.Schedule.Status != "configured")
        return Result<AttendanceTodayResponse>.Conflict("schedule_not_configured");

    if (context.PolicyStatus == "not_configured")
        return Result<AttendanceTodayResponse>.Conflict("clock_in_policy_not_configured");

    if (context.PolicyStatus == "configuration_conflict")
        return Result<AttendanceTodayResponse>.Conflict("multiple_active_company_policies");

    var source = sourceRaw.Trim().ToLowerInvariant();
    var allowed = source == AttendanceRecord.SourceWeb
        ? context.AllowedClockInMethods.Web
        : context.AllowedClockInMethods.DesktopTray;
    if (!allowed)
        return Result<AttendanceTodayResponse>.Forbidden(
            $"Clock-in source {source} is not allowed by the active policy.");

    try
    {
        var mutation = await unitOfWork.ExecuteInTransactionAsync(
            async transactionCt => await MutateAsync(context, source, transactionCt), ct);

        if (!mutation.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                mutation.Error!, mutation.StatusCode ?? 400);
    }
    catch (UniqueConstraintConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just created by another request. Please refresh and try again.");
    }
    catch (ConcurrencyConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just updated by another request. Please refresh and try again.");
    }

    return await todayState.GetTodayAsync(ct);
}
```

Note this also folds the `AllowedClockInMethods.Web` check into a source-generic check, so `HandleForContextAsync` works correctly whether it's called with `source: "web"` (from `Handle`) or `source: "tray"` (from Task 4). Check `AttendanceRecord.SourceWeb`'s exact constant value in `HRMS-Backend-v1/src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs` before writing this — it must match exactly (likely `"web"`).

- [ ] **Step 3: Run the existing test suite to confirm no regression**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ClockInCommandHandlerTests"`
Expected: All PASS unchanged — `Handle`'s externally observable behavior is identical.

- [ ] **Step 4: Add a test for the new internal entry point directly**

```csharp
[Fact]
public async Task HandleForContextAsync_TraySourceWithDesktopTrayDisabled_ReturnsForbidden()
{
    var context = BuildContext(allowedMethods: new AllowedClockInMethods(
        Web: true, DesktopTray: false, Biometric: false,
        PhotoRequired: false, LocationRequired: false, AllowedRadiusMeters: null));
    var sut = CreateSut(/* existing mocks */);

    var result = await sut.HandleForContextAsync(context, "tray", CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(403, result.StatusCode);
}
```

- [ ] **Step 5: Run it**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "HandleForContextAsync_TraySourceWithDesktopTrayDisabled_ReturnsForbidden"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommandHandler.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/Commands/ClockInCommandHandlerTests.cs
git commit -m "refactor(attendance): extract ClockInCommandHandler.HandleForContextAsync for reuse by the tray command"
```

---

## Task 4: New `TrayClockInCommand`

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockIn/TrayClockInCommand.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockIn/TrayClockInCommandHandler.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/TrayClockInCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IAttendanceTodayStateService.ResolveContextAsync(Guid, Guid, CancellationToken)` (Task 1), `ClockInCommandHandler.HandleForContextAsync(AttendanceTodayContext, string, CancellationToken)` (Task 3), `ITrayCurrentDevice` (existing).
- Produces: `TrayClockInCommand` (MediatR `IRequest<Result<AttendanceTodayResponse>>`, no properties — identity comes entirely from `ITrayCurrentDevice`, matching `GetEffectiveTrayPolicyQuery`'s existing shape), used by Task 6's controller.

- [ ] **Step 1: Write the failing test**

```csharp
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class TrayClockInCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static TrayClockInCommandHandler CreateSut(
        Mock<ITrayCurrentDevice> device,
        Mock<IAttendanceTodayStateService> todayState,
        ClockInCommandHandler inner)
    {
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);
        return new TrayClockInCommandHandler(device.Object, todayState.Object, inner);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsFailure()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(false);
        var todayState = new Mock<IAttendanceTodayStateService>();
        var sut = new TrayClockInCommandHandler(device.Object, todayState.Object, inner: null!);

        var result = await sut.Handle(new TrayClockInCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ContextResolutionFails_PropagatesFailure()
    {
        var device = new Mock<ITrayCurrentDevice>();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("no employee"));
        var sut = CreateSut(device, todayState, inner: null!);

        var result = await sut.Handle(new TrayClockInCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
```

Note the third case — "delegates to `HandleForContextAsync` with `source: \"tray\"` on success" — needs `ClockInCommandHandler` constructed with real (mocked) `IAttendanceTodayStateService`/`IAttendanceReadRepository`/`IUnitOfWork` dependencies, following the exact same `CreateSut` pattern `ClockInCommandHandlerTests.cs` already uses; copy that helper into this new test file (or extract it to a shared test helper if the two test files start diverging — YAGNI for now, duplication of one small helper method is fine).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TrayClockInCommandHandlerTests"`
Expected: FAIL to compile — `TrayClockInCommand`/`TrayClockInCommandHandler` don't exist yet.

- [ ] **Step 3: Create the command**

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayClockInCommand : IRequest<Result<AttendanceTodayResponse>>;
```

- [ ] **Step 4: Create the handler**

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class TrayClockInCommandHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    ClockInCommandHandler inner)
    : IRequestHandler<TrayClockInCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        TrayClockInCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<AttendanceTodayResponse>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        return await inner.HandleForContextAsync(contextResult.Value!, "tray", ct);
    }
}
```

`ClockInCommandHandler` is injected directly as a concrete class (not through its `IRequestHandler<ClockInCommand, ...>` interface, which MediatR's DI registration binds generically) — register it for direct injection too in Step 6 below.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TrayClockInCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Register `ClockInCommandHandler` for direct injection**

In `HRMS-Backend-v1/src/ONEVO.Infrastructure/DependencyInjection.cs`, find where MediatR handlers get assembly-scanned. If `ClockInCommandHandler` isn't also registered as itself (concrete type), add:

```csharp
services.AddScoped<ClockInCommandHandler>();
```

next to wherever other concrete-type-for-reuse registrations live in that file (search for a similar existing pattern first — if none exists, add it directly after the MediatR `AddMediatR(...)` call).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockIn/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/TrayClockInCommandHandlerTests.cs
git commit -m "feat(monitoring): add TrayClockInCommand, reusing ClockInCommandHandler's mutation logic"
```

---

## Task 5: New `TrayClockOutCommand`

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/TimeAttendance/Commands/ClockOut/ClockOutCommandHandler.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockOut/TrayClockOutCommand.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockOut/TrayClockOutCommandHandler.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/TrayClockOutCommandHandlerTests.cs`

**Interfaces:**
- Consumes: same as Task 4, plus `ClockOutCommandHandler.HandleForContextAsync(AttendanceTodayContext, CancellationToken)` (new, this task).
- Produces: `TrayClockOutCommand`, used by Task 6's controller.

Mirrors Task 3 + Task 4 exactly, except `ClockOutCommandHandler.Handle` has **no** method-eligibility check today (confirmed by reading the file — clock-out isn't source-restricted), so `HandleForContextAsync` here doesn't gain one either — it stays exactly as permissive as the existing web path.

- [ ] **Step 1: Run the existing ClockOut test suite for a green baseline**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ClockOutCommandHandlerTests"`
Expected: All PASS.

- [ ] **Step 2: Extract `HandleForContextAsync` in `ClockOutCommandHandler.cs`**

Replace:

```csharp
public async Task<Result<AttendanceTodayResponse>> Handle(
    ClockOutCommand _, CancellationToken ct)
{
    var contextResult = await todayState.ResolveContextAsync(ct);
    if (!contextResult.IsSuccess)
        return ToTodayFailure(contextResult);

    var context = contextResult.Value!;
    try
    {
        var mutation = await unitOfWork.ExecuteInTransactionAsync(
            transactionCt => MutateAsync(context, transactionCt), ct);

        if (!mutation.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                mutation.Error!, mutation.StatusCode ?? 400);
    }
    catch (ConcurrencyConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just updated by another request. Please refresh and try again.");
    }

    return await todayState.GetTodayAsync(ct);
}
```

with:

```csharp
public async Task<Result<AttendanceTodayResponse>> Handle(
    ClockOutCommand _, CancellationToken ct)
{
    var contextResult = await todayState.ResolveContextAsync(ct);
    if (!contextResult.IsSuccess)
        return ToTodayFailure(contextResult);

    return await HandleForContextAsync(contextResult.Value!, ct);
}

public async Task<Result<AttendanceTodayResponse>> HandleForContextAsync(
    AttendanceTodayContext context, CancellationToken ct)
{
    try
    {
        var mutation = await unitOfWork.ExecuteInTransactionAsync(
            transactionCt => MutateAsync(context, transactionCt), ct);

        if (!mutation.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                mutation.Error!, mutation.StatusCode ?? 400);
    }
    catch (ConcurrencyConflictException)
    {
        return Result<AttendanceTodayResponse>.Conflict(
            "Attendance for this work day was just updated by another request. Please refresh and try again.");
    }

    return await todayState.GetTodayAsync(ct);
}
```

- [ ] **Step 3: Run the existing suite to confirm no regression**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ClockOutCommandHandlerTests"`
Expected: All PASS unchanged.

- [ ] **Step 4: Write the failing test for the new tray command**

```csharp
[Fact]
public async Task Handle_NotAuthenticated_ReturnsFailure()
{
    var device = new Mock<ITrayCurrentDevice>();
    device.Setup(d => d.IsAuthenticated).Returns(false);
    var todayState = new Mock<IAttendanceTodayStateService>();
    var sut = new TrayClockOutCommandHandler(device.Object, todayState.Object, inner: null!);

    var result = await sut.Handle(new TrayClockOutCommand(), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(401, result.StatusCode);
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TrayClockOutCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 6: Create the command and handler**

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

public sealed record TrayClockOutCommand : IRequest<Result<AttendanceTodayResponse>>;
```

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class TrayClockOutCommandHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    ClockOutCommandHandler inner)
    : IRequestHandler<TrayClockOutCommand, Result<AttendanceTodayResponse>>
{
    public async Task<Result<AttendanceTodayResponse>> Handle(
        TrayClockOutCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<AttendanceTodayResponse>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<AttendanceTodayResponse>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        return await inner.HandleForContextAsync(contextResult.Value!, ct);
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TrayClockOutCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 8: Register `ClockOutCommandHandler` for direct injection**

Same as Task 4 Step 6, in `DependencyInjection.cs`:

```csharp
services.AddScoped<ClockOutCommandHandler>();
```

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/Commands/ClockOut/ClockOutCommandHandler.cs src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/TrayClockOut/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/TrayClockOutCommandHandlerTests.cs
git commit -m "feat(monitoring): add TrayClockOutCommand, reusing ClockOutCommandHandler's mutation logic"
```

---

## Task 6: New tray attendance-status query + controller endpoints

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Queries/GetTrayAttendanceStatus/GetTrayAttendanceStatusQuery.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/Queries/GetTrayAttendanceStatus/GetTrayAttendanceStatusQueryHandler.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/TrayAttendanceStatusDto.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/TrayAttendanceController.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Queries/GetTrayAttendanceStatusQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IAttendanceTodayStateService.ResolveContextAsync(Guid, Guid, CancellationToken)` (Task 1), `IAttendanceReadRepository.GetRecordAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default)` (existing), `TrayClockInCommand`/`TrayClockOutCommand` (Tasks 4, 5).
- Produces: `GET /api/v1/monitoring/tray/attendance-status`, `POST /api/v1/monitoring/tray/clock-in`, `POST /api/v1/monitoring/tray/clock-out` — the three endpoints Task 8 (Service) calls.

- [ ] **Step 1: Write the failing test**

```csharp
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Queries;

public class GetTrayAttendanceStatusQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Handle_OpenAttendanceRecord_ReturnsIsClockedInTrue()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);

        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var context = TestContextBuilder.Build(employeeId: EmployeeId, tenantId: TenantId, workDate: WorkDate);
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.GetRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { ActualStart = startedAt, ActualEnd = null });

        var sut = new GetTrayAttendanceStatusQueryHandler(device.Object, todayState.Object, attendance.Object);

        var result = await sut.Handle(new GetTrayAttendanceStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsClockedIn);
        Assert.Equal(startedAt, result.Value!.ClockedInAtUtc);
    }

    [Fact]
    public async Task Handle_NoAttendanceRecordForToday_ReturnsIsClockedInFalse()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);

        var context = TestContextBuilder.Build(employeeId: EmployeeId, tenantId: TenantId, workDate: WorkDate);
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.GetRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);

        var sut = new GetTrayAttendanceStatusQueryHandler(device.Object, todayState.Object, attendance.Object);

        var result = await sut.Handle(new GetTrayAttendanceStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsClockedIn);
        Assert.Null(result.Value!.ClockedInAtUtc);
    }
}
```

`TestContextBuilder.Build(...)` should be a small local static helper in this test file constructing a minimal valid `AttendanceTodayContext` with the given `employeeId`/`tenantId`/`workDate` and placeholder values for the rest of its fields (`Employee`, `LegalEntity`, `Schedule`, etc.) — copy the pattern from whatever helper `AttendanceTodayStateServiceTests.cs` or `ClockInCommandHandlerTests.cs` already uses to build a context for tests, rather than inventing a new one.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTrayAttendanceStatusQueryHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Create the DTO**

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

using System.Text.Json.Serialization;

public sealed record TrayAttendanceStatusDto(
    [property: JsonPropertyName("is_clocked_in")] bool IsClockedIn,
    [property: JsonPropertyName("clocked_in_at_utc")] DateTimeOffset? ClockedInAtUtc);
```

- [ ] **Step 4: Create the query and handler**

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

public sealed record GetTrayAttendanceStatusQuery : IRequest<Result<TrayAttendanceStatusDto>>;
```

```csharp
namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;

public sealed class GetTrayAttendanceStatusQueryHandler(
    ITrayCurrentDevice device,
    IAttendanceTodayStateService todayState,
    IAttendanceReadRepository attendance)
    : IRequestHandler<GetTrayAttendanceStatusQuery, Result<TrayAttendanceStatusDto>>
{
    public async Task<Result<TrayAttendanceStatusDto>> Handle(
        GetTrayAttendanceStatusQuery request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<TrayAttendanceStatusDto>.Failure("A valid tray device token is required.", 401);

        var contextResult = await todayState.ResolveContextAsync(device.TenantId, device.UserId, ct);
        if (!contextResult.IsSuccess)
            return Result<TrayAttendanceStatusDto>.Failure(
                contextResult.Error!, contextResult.StatusCode ?? 400);

        var context = contextResult.Value!;
        var record = await attendance.GetRecordAsync(
            context.Employee.TenantId, context.Employee.Id, context.WorkDate, ct);

        var isClockedIn = record?.ActualStart is not null && record.ActualEnd is null;
        return Result<TrayAttendanceStatusDto>.Success(new TrayAttendanceStatusDto(
            IsClockedIn: isClockedIn,
            ClockedInAtUtc: isClockedIn ? record!.ActualStart : null));
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTrayAttendanceStatusQueryHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Create the controller**

```csharp
namespace ONEVO.Api.Controllers.Tenant.Monitoring.CheckIn;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockOut;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;

/// <summary>
/// Tray-authenticated attendance actions. Authorization: Bearer {tray_access_token}.
/// Identity comes only from the tray JWT — never from query or body, matching
/// TrayMonitoringPolicyController.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayAttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayAttendanceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("attendance-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAttendanceStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTrayAttendanceStatusQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpPost("clock-in")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ClockIn(CancellationToken ct)
    {
        var result = await _mediator.Send(new TrayClockInCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpPost("clock-out")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClockOut(CancellationToken ct)
    {
        var result = await _mediator.Send(new TrayClockOutCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
```

- [ ] **Step 7: Manual smoke check (no integration test harness assumed)**

If `HRMS-Backend-v1` has integration tests hitting tray endpoints (check `tests/` for an existing `TrayMonitoringPolicyController`-targeting integration test as a template), add an equivalent one for `GET /api/v1/monitoring/tray/attendance-status` following that exact pattern. If none exists for the sibling policy controller either, skip — unit coverage on the handler (Step 1-5) plus manual `curl`/Postman verification against a running backend is this codebase's established bar for a new tray endpoint (see `docs/postman/ONEVO-Tray-Monitoring.postman_collection.json` — add the three new requests there too, mirroring the existing tray-policy request's auth header setup).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/Queries/GetTrayAttendanceStatus/ src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/TrayAttendanceStatusDto.cs src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/TrayAttendanceController.cs tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Queries/GetTrayAttendanceStatusQueryHandlerTests.cs
git commit -m "feat(monitoring): add tray attendance-status/clock-in/clock-out endpoints"
```

---

## Task 7: `AgentPolicy` — add `TrayClockInEnabled` (Shared)

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Shared/Models/AgentPolicy.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Shared.Tests/Models/AgentPolicyTests.cs` (create if no such file exists — check first)

**Interfaces:**
- Produces: `AgentPolicy.TrayClockInEnabled` (bool), consumed by Task 8, 9, 10, 11, 13, 14, 15.

- [ ] **Step 1: Read the current `AgentPolicy.cs` to confirm it's a plain record/class with settable properties (not positional)**

Open the file and check the shape before editing — `PolicyCache.CreateDefault()` uses object-initializer syntax (`new() { Version = ..., ... }`), so this is very likely an init-settable-property record, not a positional one; add the new property using the same style already there.

- [ ] **Step 2: Add the property**

```csharp
public bool TrayClockInEnabled { get; init; }
```

Add it in the same block as the other `bool ...Enabled` properties (`CameraVerificationEnabled`, `ScreenshotEnabled`, etc.), matching existing property order/style.

- [ ] **Step 3: Build to confirm no compile errors elsewhere**

Run (from `HRMS_TrayApp`): `dotnet build ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj`
Expected: builds clean — a new property with a default doesn't break any existing `new AgentPolicy { ... }` call site.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/Models/AgentPolicy.cs
git commit -m "feat(shared): add TrayClockInEnabled to AgentPolicy"
```

---

## Task 8: `OnevoApiClient` — map the new field and add the three new HTTP calls

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs`
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs` (check first if this file exists; if not, create it — model the stub-handler pattern on `PolicySyncServiceTests.cs`'s `StubHandler`/`StubHttpClientFactory`)

**Interfaces:**
- Produces:
  - `OnevoApiClient.GetAttendanceStatusAsync(string accessToken, CancellationToken ct)` → `Task<AttendanceStatusResult>` where `AttendanceStatusResult(bool Success, string? ErrorCode, bool IsClockedIn, DateTimeOffset? ClockedInAtUtc)`.
  - `OnevoApiClient.ClockInAsync(string accessToken, CancellationToken ct)` → `Task<ClockActionResult>` where `ClockActionResult(bool Success, string? ErrorCode, string? Message)`.
  - `OnevoApiClient.ClockOutAsync(string accessToken, CancellationToken ct)` → `Task<ClockActionResult>` (same result type).
  - Used by Task 10 (`AgentWorker`) and Task 11 (`AttendanceStatusSyncService`).

- [ ] **Step 1: Add the new routes**

In `AgentApiRoutes.cs`, next to the existing `TrayPolicy` constant:

```csharp
public const string TrayAttendanceStatus = "/api/v1/monitoring/tray/attendance-status";
public const string TrayClockIn          = "/api/v1/monitoring/tray/clock-in";
public const string TrayClockOut         = "/api/v1/monitoring/tray/clock-out";
```

- [ ] **Step 2: Write the failing test for `GetAttendanceStatusAsync`**

```csharp
[Fact]
public async Task GetAttendanceStatusAsync_Success_ReturnsIsClockedIn()
{
    var body = new { is_clocked_in = true, clocked_in_at_utc = DateTimeOffset.UtcNow };
    var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body)
    });
    var sut = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

    var result = await sut.GetAttendanceStatusAsync("device-jwt", CancellationToken.None);

    Assert.True(result.Success);
    Assert.True(result.IsClockedIn);
}

[Fact]
public async Task ClockInAsync_Forbidden_ReturnsFailureWithErrorCode()
{
    var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
    {
        Content = JsonContent.Create(new { title = "Clock-in source tray is not allowed by the active policy." })
    });
    var sut = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

    var result = await sut.ClockInAsync("device-jwt", CancellationToken.None);

    Assert.False(result.Success);
}
```

Reuse `StubHandler`/`StubHttpClientFactory` — they're already `internal`/`public` test doubles in `ONEVO.Agent.Service.Tests` (used by `PolicySyncServiceTests.cs`); confirm their exact namespace via that file's `using` list and reference the same ones rather than redefining.

- [ ] **Step 3: Run test to verify it fails**

Run (from `HRMS_TrayApp`): `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~OnevoApiClientTests"`
Expected: FAIL to compile.

- [ ] **Step 4: Implement the three methods and their wire-format payload records**

Add near the existing `GetEffectivePolicyAsync` method in `OnevoApiClient.cs`:

```csharp
public async Task<AttendanceStatusResult> GetAttendanceStatusAsync(string accessToken, CancellationToken ct)
{
    var client = _httpClientFactory.CreateClient("OnevoApi");
    using var request = new HttpRequestMessage(HttpMethod.Get, AgentApiRoutes.TrayAttendanceStatus);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(request, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.TrayAttendanceStatus);
        return new AttendanceStatusResult(false, "SERVICE_UNAVAILABLE", false, null);
    }

    if (response.StatusCode is HttpStatusCode.Unauthorized)
        return new AttendanceStatusResult(false, "UNAUTHORIZED", false, null);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("OnevoApi call to {Route} returned {Status}", AgentApiRoutes.TrayAttendanceStatus, (int)response.StatusCode);
        return new AttendanceStatusResult(false, "SERVICE_UNAVAILABLE", false, null);
    }

    TrayAttendanceStatusPayload? payload;
    try
    {
        payload = await response.Content.ReadFromJsonAsync<TrayAttendanceStatusPayload>(cancellationToken: ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", AgentApiRoutes.TrayAttendanceStatus);
        return new AttendanceStatusResult(false, "SERVICE_UNAVAILABLE", false, null);
    }

    if (payload is null)
        return new AttendanceStatusResult(false, "SERVICE_UNAVAILABLE", false, null);

    return new AttendanceStatusResult(true, null, payload.IsClockedIn, payload.ClockedInAtUtc);
}

public Task<ClockActionResult> ClockInAsync(string accessToken, CancellationToken ct) =>
    PostClockActionAsync(AgentApiRoutes.TrayClockIn, accessToken, ct);

public Task<ClockActionResult> ClockOutAsync(string accessToken, CancellationToken ct) =>
    PostClockActionAsync(AgentApiRoutes.TrayClockOut, accessToken, ct);

private async Task<ClockActionResult> PostClockActionAsync(string route, string accessToken, CancellationToken ct)
{
    var client = _httpClientFactory.CreateClient("OnevoApi");
    using var request = new HttpRequestMessage(HttpMethod.Post, route);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(request, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "OnevoApi call to {Route} failed", route);
        return new ClockActionResult(false, "SERVICE_UNAVAILABLE", null);
    }

    if (response.StatusCode is HttpStatusCode.Unauthorized)
        return new ClockActionResult(false, "UNAUTHORIZED", null);

    if (response.StatusCode is HttpStatusCode.Forbidden)
        return new ClockActionResult(false, "TRAY_CLOCK_IN_NOT_ALLOWED", null);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("OnevoApi call to {Route} returned {Status}", route, (int)response.StatusCode);
        return new ClockActionResult(false, "SERVICE_UNAVAILABLE", null);
    }

    return new ClockActionResult(true, null, null);
}
```

Near the existing `TrayAgentPolicyPayload` record at the bottom of the file, add:

```csharp
public sealed record TrayAttendanceStatusPayload(
    [property: JsonPropertyName("is_clocked_in")] bool IsClockedIn,
    [property: JsonPropertyName("clocked_in_at_utc")] DateTimeOffset? ClockedInAtUtc);

public sealed record AttendanceStatusResult(bool Success, string? ErrorCode, bool IsClockedIn, DateTimeOffset? ClockedInAtUtc);

public sealed record ClockActionResult(bool Success, string? ErrorCode, string? Message);
```

In `GetEffectivePolicyAsync`'s existing `policy = new AgentPolicy { ... }` construction and the `TrayAgentPolicyPayload` record's parameter list, add the new field:

```csharp
// TrayAgentPolicyPayload record — add trailing parameter:
[property: JsonPropertyName("tray_clock_in_enabled")] bool TrayClockInEnabled = false);

// AgentPolicy construction — add:
TrayClockInEnabled = payload.TrayClockInEnabled,
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~OnevoApiClientTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.Service/Api/OnevoApiClient.cs ONEVO.Agent.Service/Api/AgentApiRoutes.cs tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs
git commit -m "feat(service): add OnevoApiClient calls for tray attendance status/clock-in/clock-out"
```

---

## Task 9: `PolicyCache` — fail-closed default

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Policy/PolicyCache.cs`

**Interfaces:**
- No new interface — just a default-value change on an existing static factory.

- [ ] **Step 1: Add the field to `CreateDefault()`**

```csharp
public static AgentPolicy CreateDefault() => new()
{
    Version = "server-policy-unavailable",
    LocationTrackingEnabled = false,
    ActivitySignalEnabled = false,
    AppUsageEnabled = false,
    ScreenshotEnabled = false,
    InactivityScreenshotEnabled = false,
    CameraVerificationEnabled = false,
    TrayClockInEnabled = false,
    IdleThresholdMinutes = 2,
    EffectiveScope = "none",
    ValidUntil = DateTimeOffset.MinValue
};
```

- [ ] **Step 2: Build to confirm no compile errors**

Run: `dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.Service/Policy/PolicyCache.cs
git commit -m "fix(service): fail closed on TrayClockInEnabled in the default policy"
```

---

## Task 10: `AgentWorker` — real backend calls + shared presence-transition helpers

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/AgentWorker.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/AgentWorkerTests.cs` (check exact filename/location first — search `**/AgentWorkerTests.cs`)

**Interfaces:**
- Consumes: `OnevoApiClient.ClockInAsync`/`ClockOutAsync` (Task 8), `PolicyCache.Current.TrayClockInEnabled` (Tasks 7, 9).
- Produces: `AgentWorker.ApplyPresenceActive(DateTimeOffset now)` and `AgentWorker.ApplyPresenceStopped(DateTimeOffset now)` (internal) — used by Task 11's `AttendanceStatusSyncService` via a new public entry point (see Step 4 below).

- [ ] **Step 1: Locate and read the existing `AgentWorkerTests.cs` to find its test-double setup for `_lifecycleGate`/`_stateMachine`/`_apiClient`**

Search `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/**/AgentWorkerTests.cs`. Note the constructor call pattern used to build a testable `AgentWorker` — the credential store, `PolicyCache`, `OnevoApiClient` (likely with a stub `HttpMessageHandler`, same pattern as Task 8) are all already constructor parameters (confirmed in the current source), so no new DI wiring is needed here, only new stub behavior for the new HTTP calls.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task ExecuteClockIn_TrayClockInDisabled_ReturnsErrorWithoutCallingBackend()
{
    var policyCache = new PolicyCache();
    policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = false, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
    var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend when TrayClockInEnabled is false"));
    var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
    var worker = CreateSut(policyCache: policyCache, apiClient: apiClient); // existing CreateSut helper, extended with these two params

    var (success, errorCode, _, _) = await worker.HandleLifecycleForTests(LifecycleAction.ClockIn);

    Assert.False(success);
    Assert.Equal("TRAY_CLOCK_IN_NOT_ALLOWED", errorCode);
}

[Fact]
public async Task ExecuteClockIn_TrayClockInEnabledAndBackendSucceeds_TransitionsToActive()
{
    var policyCache = new PolicyCache();
    policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
    var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
    var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
    var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, allowLocalLifecycleWithoutFullGates: true);

    var (success, _, _, state) = await worker.HandleLifecycleForTests(LifecycleAction.ClockIn);

    Assert.True(success);
    Assert.Equal(MonitoringState.Active, state);
}
```

Check whether `AgentWorkerTests.cs` already has a `HandleLifecycleForTests`-style internal test entry point for driving `ExecuteClockIn` — if it currently tests via a different mechanism (e.g. directly invoking `HandleMessageAsync` with a constructed `IpcEnvelope`), follow that existing pattern instead of inventing a new one; the assertions above are what matter, not the exact invocation shape.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~AgentWorkerTests"`
Expected: The two new tests FAIL (either compile error if the test helper needs extending, or assertion failure since `ExecuteClockIn` doesn't check `TrayClockInEnabled` or call the backend yet).

- [ ] **Step 4: Refactor `ExecuteClockIn`/`ExecuteClockOut` to call the backend and extract the shared presence-transition helpers**

Replace the current `ExecuteClockIn`:

```csharp
private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockIn(
    DateTimeOffset now)
{
    var current = _stateMachine.CurrentState;
    if (current == MonitoringState.Active)
        return (false, "ALREADY_CLOCKED_IN", "You are already clocked in.", current);
    if (current == MonitoringState.Paused)
        return (false, "ON_BREAK", "End break or clock out first.", current);
    if (current == MonitoringState.Locked)
        return (false, "LOCKED", "Agent is locked. Re-enrollment required.", current);
    if (current == MonitoringState.Unenrolled)
        return (false, "UNENROLLED", "Device is not enrolled.", current);

    // Presence session must be active before CanActivate is true.
    _lifecycleGate.SetPresenceSessionActive(true);
    _lifecycleGate.SetNotOnBreak(true);

    if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
    {
        _lifecycleGate.SetPresenceSessionActive(false);
        return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);
    }

    if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
        return (false, "INVALID_STATE", $"Cannot clock in from {current}.", current);

    _presenceSession.ClockIn(now);
    return (true, null, "Clocked in successfully.", MonitoringState.Active);
}
```

with:

```csharp
private async Task<(bool Success, string? ErrorCode, string? Message, MonitoringState State)> ExecuteClockInAsync(
    DateTimeOffset now, CancellationToken ct)
{
    var current = _stateMachine.CurrentState;
    if (current == MonitoringState.Active)
        return (false, "ALREADY_CLOCKED_IN", "You are already clocked in.", current);
    if (current == MonitoringState.Paused)
        return (false, "ON_BREAK", "End break or clock out first.", current);
    if (current == MonitoringState.Locked)
        return (false, "LOCKED", "Agent is locked. Re-enrollment required.", current);
    if (current == MonitoringState.Unenrolled)
        return (false, "UNENROLLED", "Device is not enrolled.", current);

    if (!_policyCache.Current.TrayClockInEnabled)
        return (false, "TRAY_CLOCK_IN_NOT_ALLOWED", "Clock in from this device is not enabled for your work mode.", current);

    var jwt = _credentials.ReadDeviceJwt();
    if (string.IsNullOrWhiteSpace(jwt))
        return (false, "UNENROLLED", "Device is not enrolled.", current);

    var backendResult = await _apiClient.ClockInAsync(jwt, ct);
    if (!backendResult.Success)
        return (false, backendResult.ErrorCode ?? "SERVICE_UNAVAILABLE", backendResult.Message, current);

    if (!ApplyPresenceActive(now))
        return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);

    return (true, null, "Clocked in successfully.", MonitoringState.Active);
}

/// <summary>
/// Transitions local state to Active, either from a successful tray clock-in (above) or from
/// AttendanceStatusSyncService observing a backend clock-in made via another channel (web, or
/// future biometric). Still goes through LifecycleGate.CanActivate either way — a poll-detected
/// clock-in doesn't bypass consent/enrollment/etc.
/// </summary>
internal bool ApplyPresenceActive(DateTimeOffset now)
{
    var current = _stateMachine.CurrentState;
    if (current is MonitoringState.Active or MonitoringState.Paused)
        return true; // already active/paused — nothing to do

    _lifecycleGate.SetPresenceSessionActive(true);
    _lifecycleGate.SetNotOnBreak(true);

    if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
    {
        _lifecycleGate.SetPresenceSessionActive(false);
        return false;
    }

    if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
        return false;

    _presenceSession.ClockIn(now);
    return true;
}
```

Similarly replace `ExecuteClockOut`:

```csharp
private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockOut(
    DateTimeOffset now)
{
    var current = _stateMachine.CurrentState;
    if (current is not (MonitoringState.Active or MonitoringState.Paused))
        return (false, "INVALID_STATE", "You are not in an active work session.", current);

    if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
        return (false, "INVALID_STATE", "Cannot clock out.", current);

    _presenceSession.ClockOut(now);
    _lifecycleGate.SetPresenceSessionActive(false);
    _lifecycleGate.SetNotOnBreak(true);

    // ...existing SaveSessionHistory block stays exactly as-is below this point...
```

with:

```csharp
private async Task<(bool Success, string? ErrorCode, string? Message, MonitoringState State)> ExecuteClockOutAsync(
    DateTimeOffset now, CancellationToken ct)
{
    var current = _stateMachine.CurrentState;
    if (current is not (MonitoringState.Active or MonitoringState.Paused))
        return (false, "INVALID_STATE", "You are not in an active work session.", current);

    if (!_policyCache.Current.TrayClockInEnabled)
        return (false, "TRAY_CLOCK_IN_NOT_ALLOWED", "Clock out from this device is not enabled for your work mode.", current);

    var jwt = _credentials.ReadDeviceJwt();
    if (string.IsNullOrWhiteSpace(jwt))
        return (false, "UNENROLLED", "Device is not enrolled.", current);

    var backendResult = await _apiClient.ClockOutAsync(jwt, ct);
    if (!backendResult.Success)
        return (false, backendResult.ErrorCode ?? "SERVICE_UNAVAILABLE", backendResult.Message, current);

    if (!ApplyPresenceStopped(now))
        return (false, "INVALID_STATE", "Cannot clock out.", current);

    return (true, "Clocked out successfully.", null, MonitoringState.Stopped);
}

internal bool ApplyPresenceStopped(DateTimeOffset now)
{
    var current = _stateMachine.CurrentState;
    if (current is not (MonitoringState.Active or MonitoringState.Paused))
        return true; // already stopped — nothing to do

    if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
        return false;

    _presenceSession.ClockOut(now);
    _lifecycleGate.SetPresenceSessionActive(false);
    _lifecycleGate.SetNotOnBreak(true);

    // ...move the existing SaveSessionHistory try/catch block here unchanged, keeping the
    // rest of the original ExecuteClockOut body's tail exactly as it was...

    return true;
}
```

Read the remainder of the original `ExecuteClockOut` body (below `_lifecycleGate.SetNotOnBreak(true);`, the `SaveSessionHistory` block and whatever follows it up to its closing brace) before making this edit, and move that tail into `ApplyPresenceStopped` verbatim — this plan reproduces the part already shown in this file's earlier reading; re-read the live file for the exact remaining lines before editing, since a stale line could differ from what's quoted in the design spec's line-number citations by now.

Update the `payload.Action switch` dispatch (around where `ExecuteClockIn(now)`/`ExecuteClockOut(now)` are currently called) to `await`-call the renamed async versions, and make the enclosing method `async Task` if it isn't already (it already awaits other things in the same method per the earlier reading of this file, so this should already be compatible).

`TRAY_CLOCK_IN_NOT_ALLOWED` gating clock-out too is a deliberate, small deviation from the backend's own `ClockOutCommandHandler` (which has no method gate) — this is a client-side-only convenience restriction: don't let the tray UI perform a backend clock-out call for an employee whose tray isn't supposed to offer that action at all, even though the backend itself would allow it. Note this explicitly in the PR description when this task is submitted for review.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~AgentWorkerTests"`
Expected: All PASS, including the two new tests and every pre-existing one (pre-existing tests that called the old synchronous `ExecuteClockIn` behavior through the public IPC surface should still pass unchanged, since the *external* IPC contract — request/reply shape — hasn't changed, only its internal implementation and its new backend dependency, which tests must now stub via `apiClient`).

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.Service/AgentWorker.cs tests/ONEVO.Agent.Service.Tests/AgentWorkerTests.cs
git commit -m "feat(service): wire tray Clock In/Out to the real backend, extract shared presence-transition helpers"
```

---

## Task 11: New `AttendanceStatusSyncService` poller

**Files:**
- Create: `HRMS_TrayApp/ONEVO.Agent.Service/Sync/AttendanceStatusSyncService.cs`
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Program.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Sync/AttendanceStatusSyncServiceTests.cs`

**Interfaces:**
- Consumes: `OnevoApiClient.GetAttendanceStatusAsync` (Task 8), `AgentWorker.ApplyPresenceActive`/`ApplyPresenceStopped` (Task 10), `CredentialStore.ReadDeviceJwt()` (existing).
- Produces: nothing new consumed elsewhere — this is the poller itself, registered as a `BackgroundService`.

- [ ] **Step 1: Write the failing test, modeled on `PolicySyncServiceTests.cs`'s `Build`/`StubHandler` pattern**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using Xunit;

#pragma warning disable CA1001

namespace ONEVO.Agent.Service.Tests.Sync;

[Collection(CredentialStoreFileCollection.Name)]
public class AttendanceStatusSyncServiceTests
{
    [Fact]
    public async Task PollOnceAsync_BackendReportsClockedIn_CallsApplyPresenceActive()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { is_clocked_in = true, clocked_in_at_utc = DateTimeOffset.UtcNow })
        });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var applyActiveCalled = false;
        var reconciler = new RecordingPresenceReconciler(onApplyActive: () => applyActiveCalled = true);
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            apiClient,
            new CredentialStore(),
            reconciler);

        await sut.PollOnceAsync("device-jwt", CancellationToken.None);

        Assert.True(applyActiveCalled);
    }

    [Fact]
    public async Task PollOnceAsync_BackendCallFails_DoesNotChangeState()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var reconciler = new RecordingPresenceReconciler();
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            apiClient,
            new CredentialStore(),
            reconciler);

        await sut.PollOnceAsync("device-jwt", CancellationToken.None);

        Assert.Empty(reconciler.Calls);
    }
}
```

`RecordingPresenceReconciler` is a small new test double implementing the interface added in Step 3 below — put it in `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Sync/RecordingPresenceReconciler.cs`:

```csharp
namespace ONEVO.Agent.Service.Tests.Sync;

using ONEVO.Agent.Service.Sync;

public sealed class RecordingPresenceReconciler : IPresenceReconciler
{
    public List<string> Calls { get; } = [];
    private readonly Action? _onApplyActive;

    public RecordingPresenceReconciler(Action? onApplyActive = null) => _onApplyActive = onApplyActive;

    public bool ApplyPresenceActive(DateTimeOffset now)
    {
        Calls.Add("Active");
        _onApplyActive?.Invoke();
        return true;
    }

    public bool ApplyPresenceStopped(DateTimeOffset now)
    {
        Calls.Add("Stopped");
        return true;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~AttendanceStatusSyncServiceTests"`
Expected: FAIL to compile — `AttendanceStatusSyncService`/`IPresenceReconciler` don't exist yet.

- [ ] **Step 3: Add `IPresenceReconciler` and make `AgentWorker` implement it**

In `AgentWorker.cs`, add the interface it now implements (or declare it in a small new file `HRMS_TrayApp/ONEVO.Agent.Service/Sync/IPresenceReconciler.cs` and reference it):

```csharp
namespace ONEVO.Agent.Service.Sync;

public interface IPresenceReconciler
{
    bool ApplyPresenceActive(DateTimeOffset now);
    bool ApplyPresenceStopped(DateTimeOffset now);
}
```

Change `AgentWorker`'s class declaration to also implement `IPresenceReconciler`, and change the `ApplyPresenceActive`/`ApplyPresenceStopped` methods added in Task 10 from `internal` to `public` (interface implementation requires public visibility, or explicit interface implementation — use plain `public` to keep it simple and match this codebase's style elsewhere).

- [ ] **Step 4: Create `AttendanceStatusSyncService`**

```csharp
namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;

/// <summary>
/// Polls the backend's real attendance state every 60s and reconciles local MonitoringState to
/// match — the single source of truth for every employee, regardless of whether they're allowed
/// to clock in from the tray. A tray-eligible employee's local button press still gets an
/// immediate local transition (see AgentWorker.ExecuteClockInAsync); this poller is what notices
/// clock-ins/outs made via any other channel (web today, biometric later) within its cadence.
/// Modeled directly on NotificationPollingService: same PeriodicTimer shape, same
/// public-for-tests PollOnceAsync, same swallow-and-retry-next-cycle failure handling.
/// </summary>
public sealed class AttendanceStatusSyncService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<AttendanceStatusSyncService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly IPresenceReconciler _reconciler;

    public AttendanceStatusSyncService(
        ILogger<AttendanceStatusSyncService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        IPresenceReconciler reconciler)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _reconciler = reconciler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                var jwt = _credentials.ReadDeviceJwt();
                if (!string.IsNullOrWhiteSpace(jwt))
                    await PollOnceAsync(jwt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Attendance status poll failed — will retry next cycle");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Public so tests can drive one poll cycle directly without a stored Device JWT on disk.</summary>
    public async Task PollOnceAsync(string deviceJwt, CancellationToken ct)
    {
        var result = await _apiClient.GetAttendanceStatusAsync(deviceJwt, ct);
        if (!result.Success)
        {
            _logger.LogDebug("Attendance status fetch failed ({ErrorCode}) — keeping last-known local state", result.ErrorCode);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (result.IsClockedIn)
            _reconciler.ApplyPresenceActive(result.ClockedInAtUtc ?? now);
        else
            _reconciler.ApplyPresenceStopped(now);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~AttendanceStatusSyncServiceTests"`
Expected: PASS.

- [ ] **Step 6: Register the hosted service**

In `Program.cs`, next to the existing `services.AddHostedService<NotificationPollingService>();`:

```csharp
services.AddHostedService<AttendanceStatusSyncService>();
```

Also register `AgentWorker` as `IPresenceReconciler` for DI resolution — since `AgentWorker` is already registered as `AddHostedService<AgentWorker>()`, `AttendanceStatusSyncService`'s `IPresenceReconciler` constructor parameter needs its own registration resolving to the *same* `AgentWorker` singleton instance (hosted services are singletons). Add, right after the existing `services.AddHostedService<AgentWorker>();` line:

```csharp
services.AddSingleton<AgentWorker>();
services.AddSingleton<IPresenceReconciler>(sp => sp.GetRequiredService<AgentWorker>());
services.AddHostedService(sp => sp.GetRequiredService<AgentWorker>());
```

replacing the plain `services.AddHostedService<AgentWorker>();` line with the three lines above, so `AgentWorker` is resolved as one singleton shared between its hosted-service registration and its `IPresenceReconciler` registration (otherwise `AddHostedService<AgentWorker>()` alone creates its own instance separate from what `IPresenceReconciler` would resolve to).

- [ ] **Step 7: Run the full Agent Service test suite for a sanity check**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests`
Expected: All PASS.

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.Service/Sync/AttendanceStatusSyncService.cs ONEVO.Agent.Service/Sync/IPresenceReconciler.cs ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/Sync/AttendanceStatusSyncServiceTests.cs tests/ONEVO.Agent.Service.Tests/Sync/RecordingPresenceReconciler.cs
git commit -m "feat(service): add AttendanceStatusSyncService, the single source of truth for tray presence state"
```

---

## Task 12: `WorkLocationFlow` — route away from Clock In when ineligible

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Services/WorkLocationFlow.cs`
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/App.xaml.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/Services/WorkLocationFlowTests.cs` (check exact path first)

**Interfaces:**
- Produces: `WorkLocationFlow.AwaitingClockInRoute` (const string `"//awaiting-clockin"`), `WorkLocationFlow.RouteWhenStopped(IPreferencesStore prefs, bool trayClockInEnabled, DateTimeOffset? now = null)` (new required parameter) — used by Task 14's `App.xaml.cs` and by Task 13's new page.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RouteWhenStopped_TrayClockInDisabled_RoutesToAwaitingClockIn()
{
    var prefs = new InMemoryPreferencesStore(); // whatever fake IPreferencesStore this test file already uses
    WorkLocationFlow.MarkSetupComplete(prefs);

    var route = WorkLocationFlow.RouteWhenStopped(prefs, trayClockInEnabled: false);

    Assert.Equal(WorkLocationFlow.AwaitingClockInRoute, route);
}

[Fact]
public void RouteWhenStopped_TrayClockInEnabled_RoutesToLocationThenClockIn()
{
    var prefs = new InMemoryPreferencesStore();
    WorkLocationFlow.MarkSetupComplete(prefs);

    var route = WorkLocationFlow.RouteWhenStopped(prefs, trayClockInEnabled: true);

    Assert.Equal(WorkLocationFlow.LocationThenClockIn, route);
}
```

Use whatever fake `IPreferencesStore` implementation the existing `WorkLocationFlowTests.cs` (or a neighboring test file) already has — do not write a new one if one exists.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~WorkLocationFlowTests"`
Expected: FAIL to compile — `RouteWhenStopped` doesn't accept a `trayClockInEnabled` parameter yet, `AwaitingClockInRoute` doesn't exist.

- [ ] **Step 3: Update `WorkLocationFlow.cs`**

```csharp
public const string PrepareRoute = "//prepare";
public const string ClockInRoute = "//clockin";
public const string AwaitingClockInRoute = "//awaiting-clockin";
public const string LocationThenPrepare = "//location?next=prepare";
public const string LocationThenClockIn = "//location?next=clockin";
```

```csharp
public static string RouteWhenStopped(IPreferencesStore prefs, bool trayClockInEnabled, DateTimeOffset? now = null)
{
    if (!IsSetupComplete(prefs))
        return string.Empty;

    if (!trayClockInEnabled)
        return AwaitingClockInRoute;

    return RouteToStartWork(prefs, now);
}
```

`RouteToStartWork` itself stays unchanged — it's only reached once `trayClockInEnabled` is confirmed true.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~WorkLocationFlowTests"`
Expected: PASS.

- [ ] **Step 5: Update the one call site in `App.xaml.cs`**

Find the existing line:

```csharp
MonitoringState.Stopped when showEndAfterClockOut => "//end",
MonitoringState.Stopped    => WorkLocationFlow.RouteWhenStopped(_preferences),
```

and change the second line to:

```csharp
MonitoringState.Stopped    => WorkLocationFlow.RouteWhenStopped(
    _preferences, _pipeClient.LastKnownPolicy?.TrayClockInEnabled ?? false),
```

`_pipeClient` is already captured in this lambda's enclosing scope (it's used two lines above for `_pipeClient.OnStateReceived += state => { ... }` itself), so no new field/parameter is needed.

- [ ] **Step 6: Run the full TrayApp test suite for a sanity check**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests`
Expected: All PASS — check specifically for any other test that calls `WorkLocationFlow.RouteWhenStopped` with the old single-argument signature and update it to pass `trayClockInEnabled: true` (preserving prior behavior for those tests) unless the test's own intent is to also cover the new `false` case.

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/WorkLocationFlow.cs ONEVO.Agent.TrayApp/App.xaml.cs tests/ONEVO.Agent.TrayApp.Tests/Services/WorkLocationFlowTests.cs
git commit -m "feat(trayapp): route away from Clock In when TrayClockInEnabled is false"
```

---

## Task 13: New `AwaitingClockInPage`

**Files:**
- Create: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/AwaitingClockInPage.xaml`
- Create: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/AwaitingClockInPage.xaml.cs`
- Create: `HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/AwaitingClockInViewModel.cs`
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/AppShell.xaml`
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/MauiProgram.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/AwaitingClockInViewModelTests.cs`

**Interfaces:**
- No new interfaces consumed by later tasks — this is a leaf UI page.

- [ ] **Step 1: Write the failing test for the view model**

Keep this view model deliberately minimal — it has no actions of its own (the poller from Task 11 handles the actual transition away from this page automatically once the backend shows a clock-in), it just displays a static message. Model its shape on the simplest existing view model in this codebase (e.g. skim `EndSessionViewModel.cs` for the pattern of a mostly-static informational page) before writing this.

```csharp
[Fact]
public void Constructor_SetsExpectedMessage()
{
    var vm = new AwaitingClockInViewModel();

    Assert.Equal("Waiting for Clock In", vm.Title);
    Assert.Contains("web portal", vm.Message);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~AwaitingClockInViewModelTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Create the view model**

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class AwaitingClockInViewModel : BaseViewModel
{
    public AwaitingClockInViewModel()
    {
        Title = "Waiting for Clock In";
        Message = "Clock in from the ONEVO web portal to start your work session. " +
                   "This device isn't set up to clock in directly for your work mode.";
    }

    public string Message { get; }
}
```

Check `BaseViewModel`'s actual `Title` property type/accessibility (used identically by every other view model in this codebase, e.g. `ConnectWorkspaceViewModel`'s `Title = "OneXso WorkPulse";` in its constructor) before writing this — match that exact pattern.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~AwaitingClockInViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Create the page XAML**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.AwaitingClockInPage"
             x:DataType="vm:AwaitingClockInViewModel"
             Shell.NavBarIsVisible="False"
             Title="OneXso WorkPulse">

  <Grid Padding="28,16,28,12">
    <Grid.Background>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="{StaticResource BackgroundWashStart}" Offset="0" />
        <GradientStop Color="#EEF2FF" Offset="0.45" />
        <GradientStop Color="{StaticResource BackgroundWashEnd}" Offset="1" />
      </LinearGradientBrush>
    </Grid.Background>

    <Border Style="{StaticResource TrayCompactGlassCard}" Padding="24,20"
            HorizontalOptions="Center" VerticalOptions="Center" WidthRequest="420">
      <VerticalStackLayout Spacing="12">
        <Label Text="{Binding Title}" FontSize="20" FontAttributes="Bold"
               TextColor="{StaticResource TextPrimary}" HorizontalTextAlignment="Center" />
        <Label Text="{Binding Message}" FontSize="15" TextColor="{StaticResource TextSecondary}"
               HorizontalTextAlignment="Center" LineBreakMode="WordWrap" />
        <ActivityIndicator IsRunning="True" Color="{StaticResource Primary}" HorizontalOptions="Center" />
      </VerticalStackLayout>
    </Border>
  </Grid>
</ContentPage>
```

- [ ] **Step 6: Create the code-behind**

Mirror the simplest existing page's code-behind exactly (e.g. `EndSessionPage.xaml.cs`'s constructor-only shape):

```csharp
namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class AwaitingClockInPage : ContentPage
{
    public AwaitingClockInPage(AwaitingClockInViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
```

- [ ] **Step 7: Register the route, page, and view model**

In `AppShell.xaml`, add next to the `clockin` route:

```xml
<ShellContent Route="awaiting-clockin" ContentTemplate="{DataTemplate views:AwaitingClockInPage}" />
```

In `MauiProgram.cs`, add next to the existing `ClockInViewModel`/`ClockInPage` registrations:

```csharp
builder.Services.AddTransient<AwaitingClockInViewModel>();
// ...and in the pages block:
builder.Services.AddTransient<AwaitingClockInPage>();
```

- [ ] **Step 8: Build the TrayApp project to confirm the new XAML compiles**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0` (check `global.json`/the `.csproj` for the exact current target framework moniker before running — it may differ from this example; use whatever the other tasks' build commands in this codebase already use).
Expected: builds clean, or fails only with the pre-existing environment-level MAUI workload version mismatch unrelated to this change (if so, note it and move on — same caveat as this session's earlier UI work).

- [ ] **Step 9: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/AwaitingClockInPage.xaml ONEVO.Agent.TrayApp/Views/AwaitingClockInPage.xaml.cs ONEVO.Agent.TrayApp/ViewModels/AwaitingClockInViewModel.cs ONEVO.Agent.TrayApp/Views/AppShell.xaml ONEVO.Agent.TrayApp/MauiProgram.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/AwaitingClockInViewModelTests.cs
git commit -m "feat(trayapp): add AwaitingClockInPage for employees without tray clock-in access"
```

---

## Task 14: `ActiveSessionPage`/`ActiveSessionViewModel` — hide Clock Out when ineligible

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs`
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs`

**Interfaces:**
- No new interfaces consumed by later tasks.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void ShowClockOutAction_TrayClockInEnabledFalse_IsFalseEvenWhileWorking()
{
    var pipe = new FakeNamedPipeClient();
    pipe.LastKnownPolicy = new AgentPolicy { TrayClockInEnabled = false, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) };
    var vm = new ActiveSessionViewModel(pipe);
    // IsOnBreak defaults to false, so ShowWorkingActions is true — this isolates the new check.

    Assert.False(vm.ShowClockOutAction);
}

[Fact]
public void ShowClockOutAction_TrayClockInEnabledTrueAndWorking_IsTrue()
{
    var pipe = new FakeNamedPipeClient();
    pipe.LastKnownPolicy = new AgentPolicy { TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) };
    var vm = new ActiveSessionViewModel(pipe);

    Assert.True(vm.ShowClockOutAction);
}
```

Use the existing `FakeNamedPipeClient` test double (already referenced elsewhere in this test project per `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs` — check it has a settable `LastKnownPolicy` property; add one if it doesn't).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~ActiveSessionViewModelTests"`
Expected: FAIL to compile — `ShowClockOutAction` doesn't exist yet.

- [ ] **Step 3: Add the property**

In `ActiveSessionViewModel.cs`, next to the existing `ShowWorkingActions` property:

```csharp
public bool ShowWorkingActions => !IsOnBreak;

public bool ShowClockOutAction => ShowWorkingActions && (_pipe.LastKnownPolicy?.TrayClockInEnabled ?? false);
```

In `OnAppearing()`, subscribe to policy updates so this recomputes live if the policy changes mid-session (mirror the existing `_pipe.OnStatusReceived += OnStatus;` subscription pattern already in this method):

```csharp
public void OnAppearing()
{
    if (!_subscribed)
    {
        _pipe.OnStatusReceived += OnStatus;
        _pipe.OnPolicyReceived += OnPolicyReceivedForClockOutVisibility;
        _subscribed = true;
    }
    // ...rest of existing method body unchanged...
}

private void OnPolicyReceivedForClockOutVisibility(AgentPolicy _) => OnPropertyChanged(nameof(ShowClockOutAction));
```

Also unsubscribe it wherever `_pipe.OnStatusReceived -= OnStatus;` already happens in this class's disposal path (search for that line and add the matching `-=` next to it).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~ActiveSessionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Update the XAML binding**

In `ActiveSessionPage.xaml`, change the Clock Out button's containing `Border`'s `IsVisible` binding:

```xml
<Border Style="{StaticResource TraySecondaryActionBorder}" StrokeShape="RoundRectangle 16"
        StrokeThickness="0"
        IsVisible="{Binding ShowClockOutAction}">
```

(was `IsVisible="{Binding ShowWorkingActions}"`).

- [ ] **Step 6: Run the full TrayApp test suite for a sanity check**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests`
Expected: All PASS.

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs
git commit -m "feat(trayapp): hide Clock Out action when TrayClockInEnabled is false"
```

---

## Final check across both repos

- [ ] Run the full backend suite: `dotnet test tests/ONEVO.Tests.Unit` (from `HRMS-Backend-v1`) — all green.
- [ ] Run the full TrayApp + Agent Service suites: `dotnet test tests/ONEVO.Agent.Service.Tests` and `dotnet test tests/ONEVO.Agent.TrayApp.Tests` (from `HRMS_TrayApp`) — all green.
- [ ] Add the three new tray endpoints to `HRMS_TrayApp/docs/postman/ONEVO-Tray-Monitoring.postman_collection.json`, mirroring the existing tray-policy request's headers/auth setup, so manual verification against a running backend doesn't require reconstructing the requests from scratch.
- [ ] Manually verify end-to-end against a locally running backend + Agent Service + TrayApp (see `HRMS_TrayApp/scripts/run-all.ps1` for the launch sequence, adjusting its hardcoded `C:\HR\...` paths to this machine's actual `C:\onevoNew\...` layout first): an employee whose work mode has `DesktopTray=false` sees the new awaiting-clock-in page and no Clock In button; clocking in via the web portal transitions the tray to the Active page within ~60 seconds without any tray interaction; an employee with `DesktopTray=true` can still clock in directly from the tray and it now writes a real `AttendanceRecord`.
