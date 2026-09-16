# Device Change Approval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce one active TrayApp device per employee. Enrolling from a
different device is blocked and automatically raises an approval request
(resolved via the existing reporting-coverage approver logic) that surfaces
on the existing web Requests screen; approving it retires the old device and
lets the new one enroll.

**Architecture:** A new tenant-owned `DeviceChangeRequest` entity, following
the exact same shape/pattern as the existing `LocationChangeRequest`. The
mismatch is detected once, in `TrayEnrollmentService.IssueAsync` (the single
choke point both the browser-pairing and manual-activation-code paths call),
which raises a typed exception the two calling handlers translate into a
`device_change_pending` failure. A new workflow/controller pair, mirroring
`LocationChangeRequestWorkflow`/`LocationChangeRequestsController`, lets an
approver (resolved via the existing `IEmployeeAuthorityResolver`) approve or
reject. Approval deactivates the old device registration and revokes its
tokens using repository methods that already exist.

**Tech Stack:** .NET 8 / EF Core / PostgreSQL (HRMS-Backend-v1, xUnit),
Angular (Hrms--Web-application---front-end---v1, Jasmine/Karma or Vitest —
match whatever the existing Location approvals spec uses), .NET MAUI
(HRMS_TrayApp, xUnit).

**Spec:** [docs/superpowers/specs/2026-09-16-device-change-approval-design.md](../specs/2026-09-16-device-change-approval-design.md)

## Global Constraints

- New table must have RLS policy coverage in the *same* migration that
  creates it — `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage`
  will fail otherwise (this exact gap is why `location_change_requests`
  needed a follow-up RLS migration; don't repeat it).
- Approval permission is `attendance:approve` — the same one the other
  three Requests tabs use. No new permission string.
- No new inbound TrayApp endpoint for submitting a device-change request —
  it is created server-side inside `TrayEnrollmentService.IssueAsync` when a
  mismatch is detected, never by an explicit client call.
- Do not touch `RefreshTrayTokenCommandHandler`'s existing fingerprint-replay
  check (`RefreshTrayTokenCommandHandler.cs:63`) — unrelated, stays as is.

---

### Task 1: `DeviceChangeRequest` entity, EF configuration, repository, migration

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/DeviceChangeRequest.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceChangeRequestConfiguration.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/IDeviceChangeRequestRepository.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/EfDeviceChangeRequestRepository.cs`
- Modify: `HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add `DbSet<DeviceChangeRequest> DeviceChangeRequests`
- Create migration via `dotnet ef migrations add AddDeviceChangeRequests` (run from `HRMS-Backend-v1/src/ONEVO.Api` or wherever existing migrations are added from — check a recent `dotnet ef` invocation in this repo's docs/scripts if unsure)
- Test: `HRMS-Backend-v1/tests/ONEVO.Infrastructure.Tests/Persistence/Repositories/Monitoring/EfDeviceChangeRequestRepositoryTests.cs`

**Interfaces:**
- Produces: `DeviceChangeRequest` (fields below), `IDeviceChangeRequestRepository` with the methods listed in Step 3 — later tasks (2, 5, 6) depend on these exact names.

- [ ] **Step 1: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public sealed class DeviceChangeRequest : ITenantOwnedEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public Guid? CurrentDeviceRegistrationId { get; set; }
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

- [ ] **Step 2: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring;

public sealed class DeviceChangeRequestConfiguration : IEntityTypeConfiguration<DeviceChangeRequest>
{
    public void Configure(EntityTypeBuilder<DeviceChangeRequest> builder)
    {
        builder.ToTable("device_change_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NewDeviceFingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NewDeviceName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NewDeviceOs).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ReviewComment).HasColumnType("text");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_device_change_requests_tenant_status");
        // At most one PENDING request per employee — a repeat mismatch attempt
        // updates the existing pending row instead of inserting a duplicate.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ux_device_change_requests_pending_employee");

        builder.HasOne<ONEVO.Domain.Features.Monitoring.TrayActivation.Entities.TrayDeviceRegistration>()
            .WithMany().HasForeignKey(x => x.CurrentDeviceRegistrationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ONEVO.Domain.Features.InfrastructureModule.Entities.User>()
            .WithMany().HasForeignKey(x => x.ReviewedById).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Write the repository interface**

```csharp
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

public interface IDeviceChangeRequestRepository
{
    /// <summary>Inserts a new pending request, or if one is already pending for this
    /// employee, updates its NewDeviceFingerprint/NewDeviceName/NewDeviceOs/RequestedAt
    /// in place instead of inserting a duplicate.</summary>
    Task UpsertPendingAsync(DeviceChangeRequest request, CancellationToken ct = default);

    Task<DeviceChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task<(IReadOnlyList<DeviceChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the EF repository**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring;

public sealed class EfDeviceChangeRequestRepository(ApplicationDbContext db) : IDeviceChangeRequestRepository
{
    public async Task UpsertPendingAsync(DeviceChangeRequest request, CancellationToken ct = default)
    {
        var existing = await db.DeviceChangeRequests.SingleOrDefaultAsync(x =>
            x.TenantId == request.TenantId && x.EmployeeId == request.EmployeeId
            && x.Status == DeviceChangeRequest.StatusPending, ct);

        if (existing is null)
        {
            await db.DeviceChangeRequests.AddAsync(request, ct);
            return;
        }

        existing.NewDeviceFingerprint = request.NewDeviceFingerprint;
        existing.NewDeviceName = request.NewDeviceName;
        existing.NewDeviceOs = request.NewDeviceOs;
        existing.CurrentDeviceRegistrationId = request.CurrentDeviceRegistrationId;
        existing.RequestedAt = request.RequestedAt;
    }

    public Task<DeviceChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.DeviceChangeRequests.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public async Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default)
        => await db.DeviceChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && x.Status == DeviceChangeRequest.StatusPending)
            .Select(x => x.EmployeeId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<DeviceChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return (Array.Empty<DeviceChangeRequest>(), 0);

        var query = db.DeviceChangeRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LegalEntityId == legalEntityId
                && employeeIds.Contains(x.EmployeeId)
                && x.Status == DeviceChangeRequest.StatusPending);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
```

Register both in the DI composition root next to the other Tray/Monitoring
repositories (find the existing `AddScoped<ITrayActivationRepository, ...>()`
line and add `AddScoped<IDeviceChangeRequestRepository, EfDeviceChangeRequestRepository>()`
beside it), and add `public DbSet<DeviceChangeRequest> DeviceChangeRequests => Set<DeviceChangeRequest>();`
to `ApplicationDbContext`.

- [ ] **Step 5: Generate the migration and add RLS policy coverage in the same file**

```bash
cd HRMS-Backend-v1/src/ONEVO.Api
dotnet ef migrations add AddDeviceChangeRequests --project ../ONEVO.Infrastructure --startup-project .
```

Open the generated migration and append RLS coverage in `Up`/`Down`
(matching `AddLocationChangeRequestsRlsPolicyCoverage.cs` exactly, just for
the one new table):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ... EF-generated CreateTable(...) call stays above this ...

    migrationBuilder.Sql(@"
        ALTER TABLE device_change_requests ENABLE ROW LEVEL SECURITY;
        ALTER TABLE device_change_requests FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS tenant_isolation ON device_change_requests;
        CREATE POLICY tenant_isolation ON device_change_requests
            USING (
                current_setting('app.tenant_context_mode', true) = 'admin'
                OR (
                    current_setting('app.tenant_context_mode', true) = 'tenant'
                    AND tenant_id::text = current_setting('app.current_tenant_id', true)
                )
            )
            WITH CHECK (
                current_setting('app.tenant_context_mode', true) = 'admin'
                OR (
                    current_setting('app.tenant_context_mode', true) = 'tenant'
                    AND tenant_id::text = current_setting('app.current_tenant_id', true)
                )
            );
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        DROP POLICY IF EXISTS tenant_isolation ON device_change_requests;
        ALTER TABLE device_change_requests DISABLE ROW LEVEL SECURITY;
    ");

    // ... EF-generated DropTable(...) call stays below this ...
}
```

- [ ] **Step 6: Write the failing repository test**

```csharp
[Fact]
public async Task UpsertPendingAsync_WhenNoPendingExists_InsertsNewRow()
{
    var request = new DeviceChangeRequest
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
        NewDeviceFingerprint = "fp-new", NewDeviceName = "New PC", NewDeviceOs = "Windows",
        RequestedAt = DateTimeOffset.UtcNow,
    };

    await Repository.UpsertPendingAsync(request);
    await Repository.SaveChangesAsync();

    var saved = await Repository.GetTrackedByIdAsync(TenantId, request.Id);
    Assert.NotNull(saved);
    Assert.Equal(DeviceChangeRequest.StatusPending, saved!.Status);
}

[Fact]
public async Task UpsertPendingAsync_WhenPendingAlreadyExists_UpdatesInPlaceInsteadOfDuplicating()
{
    var first = new DeviceChangeRequest
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
        NewDeviceFingerprint = "fp-1", NewDeviceName = "PC 1", NewDeviceOs = "Windows",
        RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
    };
    await Repository.UpsertPendingAsync(first);
    await Repository.SaveChangesAsync();

    var second = new DeviceChangeRequest
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
        NewDeviceFingerprint = "fp-2", NewDeviceName = "PC 2", NewDeviceOs = "Windows",
        RequestedAt = DateTimeOffset.UtcNow,
    };
    await Repository.UpsertPendingAsync(second);
    await Repository.SaveChangesAsync();

    var (items, totalCount) = await Repository.ListApprovalInboxAsync(
        TenantId, LegalEntityId, new[] { EmployeeId }, 0, 10);
    Assert.Equal(1, totalCount);
    Assert.Equal("fp-2", items[0].NewDeviceFingerprint);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Infrastructure.Tests --filter EfDeviceChangeRequestRepositoryTests`
Expected: FAIL (repository/entity don't exist yet).

- [ ] **Step 7: Confirm it passes after Steps 1-5**

Run the same command. Expected: PASS.

- [ ] **Step 8: Run the RLS architecture test**

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Infrastructure.Tests --filter TenantIsolationArchitectureTests`
Expected: PASS (device_change_requests now has coverage from the same migration).

- [ ] **Step 9: Apply the migration to the local dev database**

```bash
dotnet ef database update --project HRMS-Backend-v1/src/ONEVO.Infrastructure --startup-project HRMS-Backend-v1/src/ONEVO.Api
```

- [ ] **Step 10: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Domain/Features/Monitoring/TrayActivation/Entities/DeviceChangeRequest.cs HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/DeviceChangeRequestConfiguration.cs HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/IDeviceChangeRequestRepository.cs HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/EfDeviceChangeRequestRepository.cs HRMS-Backend-v1/src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs HRMS-Backend-v1/src/ONEVO.Infrastructure/Migrations HRMS-Backend-v1/tests/ONEVO.Infrastructure.Tests/Persistence/Repositories/Monitoring/EfDeviceChangeRequestRepositoryTests.cs
git commit -m "feat(backend): add DeviceChangeRequest entity, repository, and migration"
```

---

### Task 2: `EmployeeAuthorityPurpose.DeviceChangeApproval`

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs`

**Interfaces:**
- Produces: `EmployeeAuthorityPurpose.DeviceChangeApproval` — consumed by Task 5.

- [ ] **Step 1: Add the enum value**

```csharp
public enum EmployeeAuthorityPurpose
{
    EmployeeListRead,
    TimeTrackingRead,
    AttendanceCorrectionApproval,
    WorkAreaChangeApproval,
    LocationChangeApproval,
    TimeOffApproval,
    OnboardingApproval,
    OffboardingApproval,
    EmployeeLifecycleApproval,
    DeviceChangeApproval,
}
```

No test needed for a pure enum addition (per the type's own doc comment:
"adding a new value never requires a migration" — it's a call-site hint, not
persisted or serialized anywhere that a new value could break).

- [ ] **Step 2: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs
git commit -m "feat(backend): add DeviceChangeApproval authority purpose"
```

---

### Task 3: Detect the mismatch in `TrayEnrollmentService.IssueAsync`

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Exceptions/DeviceChangePendingException.cs`
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs:26-93`
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/RepositoryInterfaces/ITrayActivationRepository.cs` (no change needed — `FindLatestActiveDeviceForUserAsync` already exists at line 36)
- Test: `HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Services/TrayEnrollmentServiceTests.cs` (extend existing file if present, else create)

**Interfaces:**
- Consumes: `ITrayActivationRepository.FindLatestActiveDeviceForUserAsync(Guid userId, Guid tenantId, CancellationToken ct)` (existing), `IDeviceChangeRequestRepository.UpsertPendingAsync` (Task 1).
- Produces: `DeviceChangePendingException` — consumed by Task 4's handlers.

- [ ] **Step 1: Write the exception type**

```csharp
namespace ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;

/// <summary>Thrown by TrayEnrollmentService.IssueAsync when the enrolling device's
/// fingerprint doesn't match the employee's current active device. A DeviceChangeRequest
/// has already been created/updated by the time this is thrown - callers should not retry
/// IssueAsync, only surface the pending-approval state to the client.</summary>
public sealed class DeviceChangePendingException : Exception
{
    public DeviceChangePendingException()
        : base("A different device is already approved for this employee; a change request has been raised.")
    {
    }
}
```

- [ ] **Step 2: Write the failing unit tests**

```csharp
[Fact]
public async Task IssueAsync_WhenNoActiveDeviceExists_IssuesCredentialsNormally()
{
    Repository.FindLatestActiveDeviceForUserAsyncResult = null; // test double: no existing device

    var result = await Service.IssueAsync(ValidRequest, CancellationToken.None);

    Assert.NotNull(result.AccessToken);
    Assert.Empty(DeviceChangeRequests.Inserted);
}

[Fact]
public async Task IssueAsync_WhenSameFingerprintAsActiveDevice_IssuesCredentialsNormally()
{
    Repository.FindLatestActiveDeviceForUserAsyncResult = new TrayDeviceRegistration
    {
        Id = Guid.NewGuid(), UserId = ValidRequest.UserId, TenantId = ValidRequest.TenantId,
        DeviceFingerprint = ValidRequest.DeviceFingerprint, IsActive = true,
    };

    var result = await Service.IssueAsync(ValidRequest, CancellationToken.None);

    Assert.NotNull(result.AccessToken);
    Assert.Empty(DeviceChangeRequests.Inserted);
}

[Fact]
public async Task IssueAsync_WhenDifferentFingerprintThanActiveDevice_RaisesRequestAndThrows()
{
    var existingDevice = new TrayDeviceRegistration
    {
        Id = Guid.NewGuid(), UserId = ValidRequest.UserId, TenantId = ValidRequest.TenantId,
        DeviceFingerprint = "fp-old", IsActive = true,
    };
    Repository.FindLatestActiveDeviceForUserAsyncResult = existingDevice;

    await Assert.ThrowsAsync<DeviceChangePendingException>(
        () => Service.IssueAsync(ValidRequest, CancellationToken.None));

    var raised = Assert.Single(DeviceChangeRequests.Inserted);
    Assert.Equal(DeviceChangeRequest.StatusPending, raised.Status);
    Assert.Equal(existingDevice.Id, raised.CurrentDeviceRegistrationId);
    Assert.Equal(ValidRequest.DeviceFingerprint, raised.NewDeviceFingerprint);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter TrayEnrollmentServiceTests`
Expected: FAIL (constructor doesn't take `IDeviceChangeRequestRepository` yet; `DeviceChangePendingException` doesn't exist before Step 1, guard doesn't exist before Step 3).

- [ ] **Step 3: Add the guard to `TrayEnrollmentService`**

Inject `IDeviceChangeRequestRepository` (add to the constructor's existing parameter list as `_deviceChangeRequests`), then at the top of `IssueAsync` before the current `var device = new TrayDeviceRegistration { ... }` line:

```csharp
public async Task<TrayAuthResponseDto> IssueAsync(
    TrayEnrollmentRequest request,
    CancellationToken ct)
{
    var existingDevice = await _repository.FindLatestActiveDeviceForUserAsync(
        request.UserId, request.TenantId, ct);

    if (existingDevice is not null && existingDevice.DeviceFingerprint != request.DeviceFingerprint)
    {
        await _deviceChangeRequests.UpsertPendingAsync(new DeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            EmployeeId = request.UserId,
            LegalEntityId = request.LegalEntityId,
            CurrentDeviceRegistrationId = existingDevice.Id,
            NewDeviceFingerprint = request.DeviceFingerprint,
            NewDeviceName = request.DeviceName,
            NewDeviceOs = request.DeviceOs,
            Status = DeviceChangeRequest.StatusPending,
            RequestedAt = _clock.UtcNow,
        }, ct);
        await _deviceChangeRequests.SaveChangesAsync(ct);
        throw new DeviceChangePendingException();
    }

    var now = _clock.UtcNow;
    var device = new TrayDeviceRegistration
    {
        // ... unchanged from here down ...
```

- [ ] **Step 4: Run tests, confirm pass**

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter TrayEnrollmentServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Exceptions/DeviceChangePendingException.cs HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Services/TrayEnrollmentServiceTests.cs
git commit -m "feat(backend): block enrollment from a different device and raise a change request"
```

---

### Task 4: Propagate the block through both enrollment handlers

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/PollDeviceAuthorization/PollDeviceAuthorizationCommandHandler.cs:86-98`
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs:56-67`
- Test: extend existing test files for both handlers (locate via `PollDeviceAuthorizationCommandHandlerTests.cs` / `ExchangeActivationCodeCommandHandlerTests.cs` under `HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Commands/`)

**Interfaces:**
- Consumes: `DeviceChangePendingException` (Task 3).

- [ ] **Step 1: Write the failing tests**

```csharp
// PollDeviceAuthorizationCommandHandlerTests.cs
[Fact]
public async Task Handle_WhenEnrollmentServiceThrowsDeviceChangePending_ReturnsDeviceChangePendingFailure()
{
    EnrollmentService.IssueAsyncThrows = new DeviceChangePendingException();

    var result = await Handler.Handle(ApprovedPollCommand, CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal("device_change_pending", result.ErrorCode);
}
```

```csharp
// ExchangeActivationCodeCommandHandlerTests.cs
[Fact]
public async Task Handle_WhenEnrollmentServiceThrowsDeviceChangePending_ReturnsDeviceChangePendingFailure()
{
    EnrollmentService.IssueAsyncThrows = new DeviceChangePendingException();

    var result = await Handler.Handle(ValidExchangeCommand, CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal("device_change_pending", result.ErrorCode);
}
```

Run both test files. Expected: FAIL (handlers don't catch the exception yet).

- [ ] **Step 2: Catch it in `PollDeviceAuthorizationCommandHandler.Handle`**

Wrap the existing `_enrollmentService.IssueAsync(...)` call (line 86-94):

```csharp
TrayAuthResponseDto credentials;
try
{
    credentials = await _enrollmentService.IssueAsync(
        new TrayEnrollmentRequest(
            authorization.ApprovedTenantId.Value,
            authorization.ApprovedUserId.Value,
            authorization.ApprovedLegalEntityId,
            authorization.DeviceName,
            authorization.DeviceOs,
            request.DeviceFingerprint),
        innerCt);
}
catch (DeviceChangePendingException)
{
    authorization.Status = DeviceAuthorizationStatus.Consumed;
    authorization.ConsumedAt = now;
    await _unitOfWork.SaveChangesAsync(innerCt);
    return Failure("device_change_pending",
        "A different device is already approved for your account. A request has been sent for approval.");
}

authorization.Status = DeviceAuthorizationStatus.Consumed;
authorization.ConsumedAt = now;
await _unitOfWork.SaveChangesAsync(innerCt);
return Result<TrayAuthResponseDto>.Success(credentials);
```

- [ ] **Step 3: Catch it in `ExchangeActivationCodeCommandHandler.Handle`**

Wrap the existing `_enrollmentService.IssueAsync(...)` call (line 56-64):

```csharp
TrayAuthResponseDto credentials;
try
{
    credentials = await _enrollmentService.IssueAsync(
        new TrayEnrollmentRequest(
            activationCode.TenantId,
            activationCode.UserId,
            activationCode.LegalEntityId,
            request.DeviceName,
            request.DeviceOs,
            request.DeviceFingerprint),
        ct);
}
catch (DeviceChangePendingException)
{
    await _unitOfWork.SaveChangesAsync(ct);
    return Result<TrayAuthResponseDto>.Failure(
        "A different device is already approved for your account. A request has been sent for approval.",
        409, "device_change_pending");
}

await _unitOfWork.SaveChangesAsync(ct);
return Result<TrayAuthResponseDto>.Success(credentials);
```

- [ ] **Step 4: Run tests, confirm pass**

Run both handler test files. Expected: PASS. Also re-run the full
`PollDeviceAuthorizationCommandHandlerTests`/`ExchangeActivationCodeCommandHandlerTests`
suites to confirm the pre-existing passing cases still pass.

- [ ] **Step 5: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/PollDeviceAuthorization/PollDeviceAuthorizationCommandHandler.cs HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/ExchangeActivationCode/ExchangeActivationCodeCommandHandler.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Commands
git commit -m "feat(backend): surface device_change_pending from both enrollment paths"
```

---

### Task 5: `DeviceChangeRequestWorkflow` (approve/reject/list)

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/DeviceChangeRequests/DeviceChangeRequestCommands.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Queries/DeviceChangeRequests/DeviceChangeRequestQueries.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/DeviceChangeRequestResponse.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/DeviceChangeRequests/DeviceChangeRequestWorkflow.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Commands/DeviceChangeRequests/DeviceChangeRequestWorkflowTests.cs`

**Interfaces:**
- Consumes: `IDeviceChangeRequestRepository` (Task 1), `EmployeeAuthorityPurpose.DeviceChangeApproval` (Task 2), `ITrayActivationRepository.DeactivateDeviceAsync`/`RevokeAllRefreshTokensForDeviceAsync` (existing, `ITrayActivationRepository.cs:33,38`).
- Produces: `ApproveDeviceChangeRequestCommand`, `RejectDeviceChangeRequestCommand`, `ListDeviceChangeRequestApprovalsQuery`, `DeviceChangeRequestResponse`, `DeviceChangeRequestWorkflow` with `ApproveAsync`/`RejectAsync`/`ListApprovalsAsync` — consumed by Task 6.

- [ ] **Step 1: Write the response DTO**

```csharp
namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public sealed record DeviceChangeRequestResponse(
    Guid Id,
    Guid EmployeeId,
    string RequesterDisplayName,
    string NewDeviceName,
    string NewDeviceOs,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ReviewedById,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment);
```

- [ ] **Step 2: Write commands and query**

```csharp
// DeviceChangeRequestCommands.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;

public sealed record ApproveDeviceChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<DeviceChangeRequestResponse>>;

public sealed record RejectDeviceChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<DeviceChangeRequestResponse>>;
```

```csharp
// DeviceChangeRequestQueries.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;

public sealed record ListDeviceChangeRequestApprovalsQuery(
    PagedRequest Paging) : IRequest<Result<PagedResult<DeviceChangeRequestResponse>>>;
```

- [ ] **Step 3: Write the failing workflow tests**

```csharp
[Fact]
public async Task ApproveAsync_DeactivatesOldDeviceRevokesTokensAndMarksApproved()
{
    var request = PendingRequest(currentDeviceRegistrationId: DeviceId);
    Requests.GetTrackedByIdResult = request;
    Authority.ResolveApproverAsyncResult = ApprovedRoute(CurrentUser.UserId);

    var result = await Workflow.ApproveAsync(new ApproveDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(DeviceChangeRequest.StatusApproved, request.Status);
    Assert.Contains(DeviceId, TrayActivationRepository.DeactivatedDeviceIds);
    Assert.Contains(DeviceId, TrayActivationRepository.RevokedAllTokensForDeviceIds);
}

[Fact]
public async Task ApproveAsync_WhenCallerIsNotTheResolvedApprover_ReturnsForbidden()
{
    var request = PendingRequest(currentDeviceRegistrationId: DeviceId);
    Requests.GetTrackedByIdResult = request;
    Authority.ResolveApproverAsyncResult = ApprovedRoute(Guid.NewGuid()); // someone else

    var result = await Workflow.ApproveAsync(new ApproveDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(403, result.StatusCode);
    Assert.NotEqual(DeviceChangeRequest.StatusApproved, request.Status);
}

[Fact]
public async Task RejectAsync_RequiresReviewCommentAndLeavesDeviceUntouched()
{
    var request = PendingRequest(currentDeviceRegistrationId: DeviceId);
    Requests.GetTrackedByIdResult = request;

    var result = await Workflow.RejectAsync(new RejectDeviceChangeRequestCommand(request.Id, null), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Empty(TrayActivationRepository.DeactivatedDeviceIds);
}

[Fact]
public async Task RejectAsync_WithComment_MarksRejected()
{
    var request = PendingRequest(currentDeviceRegistrationId: DeviceId);
    Requests.GetTrackedByIdResult = request;
    Authority.ResolveApproverAsyncResult = ApprovedRoute(CurrentUser.UserId);

    var result = await Workflow.RejectAsync(new RejectDeviceChangeRequestCommand(request.Id, "Not their device"), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(DeviceChangeRequest.StatusRejected, request.Status);
    Assert.Empty(TrayActivationRepository.DeactivatedDeviceIds);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter DeviceChangeRequestWorkflowTests`
Expected: FAIL (`DeviceChangeRequestWorkflow` doesn't exist yet).

- [ ] **Step 4: Write the workflow**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;

public sealed class DeviceChangeRequestWorkflow(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    IDeviceChangeRequestRepository requests,
    ITrayActivationRepository trayActivation,
    IEmployeeAuthorityResolver authority)
{
    private const string ApprovalPermission = "attendance:approve";

    public Task<Result<DeviceChangeRequestResponse>> ApproveAsync(
        ApproveDeviceChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, DeviceChangeRequest.StatusApproved, command.ReviewComment, ct);

    public Task<Result<DeviceChangeRequestResponse>> RejectAsync(
        RejectDeviceChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, DeviceChangeRequest.StatusRejected, command.ReviewComment, ct);

    public async Task<Result<PagedResult<DeviceChangeRequestResponse>>> ListApprovalsAsync(
        ListDeviceChangeRequestApprovalsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<PagedResult<DeviceChangeRequestResponse>>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<PagedResult<DeviceChangeRequestResponse>>.Forbidden(
                "You do not have permission to approve device changes.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee?.LegalEntityId is null)
            return Result<PagedResult<DeviceChangeRequestResponse>>.NotFound("Current employee record was not found.");

        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                employee.LegalEntityId.Value, ApprovalPermission,
                EmployeeAuthorityPurpose.DeviceChangeApproval, candidateEmployeeIds), ct);

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, eligibleEmployeeIds,
            (pageNumber - 1) * pageSize, pageSize, ct);
        var employeeMap = await employees.ListByIdsAsync(
            currentUser.TenantId, rows.Select(r => r.EmployeeId).Distinct().ToArray(), ct);
        var items = rows.Select(row =>
            ToResponse(row, employeeMap.TryGetValue(row.EmployeeId, out var requester) ? requester : null)).ToArray();
        return Result<PagedResult<DeviceChangeRequestResponse>>.Success(
            new PagedResult<DeviceChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    private async Task<Result<DeviceChangeRequestResponse>> DecideAsync(
        Guid id, string decision, string? reviewComment, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<DeviceChangeRequestResponse>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<DeviceChangeRequestResponse>.Forbidden("You do not have permission to approve device changes.");
        if (decision == DeviceChangeRequest.StatusRejected && string.IsNullOrWhiteSpace(reviewComment))
            return Result<DeviceChangeRequestResponse>.Failure("A review comment is required when rejecting a request.");

        var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, id, ct);
        if (request is null)
            return Result<DeviceChangeRequestResponse>.NotFound("Device change request was not found.");
        if (request.Status != DeviceChangeRequest.StatusPending)
            return Result<DeviceChangeRequestResponse>.Conflict("Only a pending device change request can be reviewed.");

        var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            request.EmployeeId, request.LegalEntityId, ApprovalPermission,
            EmployeeAuthorityPurpose.DeviceChangeApproval), ct);
        if (!route.IsSuccess || route.Value is null)
            return Result<DeviceChangeRequestResponse>.Conflict("No eligible device-change approver is configured for this employee.");
        if (route.Value.ApproverUserId != currentUser.UserId)
            return Result<DeviceChangeRequestResponse>.Forbidden("You are not an eligible approver for this request.");

        var employee = await employees.GetByIdAsync(currentUser.TenantId, request.EmployeeId, ct);

        if (decision == DeviceChangeRequest.StatusApproved && request.CurrentDeviceRegistrationId is Guid oldDeviceId)
        {
            await trayActivation.DeactivateDeviceAsync(oldDeviceId, dateTime.UtcNow, ct);
            await trayActivation.RevokeAllRefreshTokensForDeviceAsync(oldDeviceId, "device_change_approved", ct);
        }

        request.Status = decision;
        request.ReviewedById = currentUser.UserId;
        request.ReviewedAt = dateTime.UtcNow;
        request.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();
        await requests.SaveChangesAsync(ct);

        return Result<DeviceChangeRequestResponse>.Success(ToResponse(request, employee));
    }

    private static string DisplayName(EmployeeEntity? employee)
        => employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();

    private static DeviceChangeRequestResponse ToResponse(DeviceChangeRequest request, EmployeeEntity? employee)
        => new(
            request.Id, request.EmployeeId, DisplayName(employee),
            request.NewDeviceName, request.NewDeviceOs, request.Status,
            request.RequestedAt, request.ReviewedById, request.ReviewedAt, request.ReviewComment);
}

public sealed class ApproveDeviceChangeRequestCommandHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<ApproveDeviceChangeRequestCommand, Result<DeviceChangeRequestResponse>>
{
    public Task<Result<DeviceChangeRequestResponse>> Handle(ApproveDeviceChangeRequestCommand request, CancellationToken ct)
        => workflow.ApproveAsync(request, ct);
}

public sealed class RejectDeviceChangeRequestCommandHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<RejectDeviceChangeRequestCommand, Result<DeviceChangeRequestResponse>>
{
    public Task<Result<DeviceChangeRequestResponse>> Handle(RejectDeviceChangeRequestCommand request, CancellationToken ct)
        => workflow.RejectAsync(request, ct);
}

public sealed class ListDeviceChangeRequestApprovalsQueryHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<ListDeviceChangeRequestApprovalsQuery, Result<PagedResult<DeviceChangeRequestResponse>>>
{
    public Task<Result<PagedResult<DeviceChangeRequestResponse>>> Handle(ListDeviceChangeRequestApprovalsQuery request, CancellationToken ct)
        => workflow.ListApprovalsAsync(request, ct);
}
```

Note: `IEmployeeRepository.GetDefaultForUserAsync`/`GetByIdAsync`/`ListByIdsAsync`
are the same methods `LocationChangeRequestWorkflow` already depends on
(`CoreEmployeeRepository` alias there) — reuse the unaliased interface
directly here.

- [ ] **Step 5: Run tests, confirm pass**

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter DeviceChangeRequestWorkflowTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/DeviceChangeRequests HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Queries/DeviceChangeRequests HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/DeviceChangeRequestResponse.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Commands/DeviceChangeRequests
git commit -m "feat(backend): add device change request approve/reject/list workflow"
```

---

### Task 6: `DeviceChangeRequestsController`

**Files:**
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Contracts/Monitoring/DeviceChangeRequests/DeviceChangeRequestRequests.cs`
- Create: `HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceChangeRequestsController.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Api.Tests/Controllers/Tenant/Monitoring/DeviceChangeRequestsControllerTests.cs` (or the repo's equivalent HTTP-integration-test project/location — check where `LocationChangeRequestsController`'s tests live and mirror that project)

**Interfaces:**
- Consumes: `ApproveDeviceChangeRequestCommand`, `RejectDeviceChangeRequestCommand`, `ListDeviceChangeRequestApprovalsQuery` (Task 5).

- [ ] **Step 1: Write the request contract**

```csharp
namespace ONEVO.Api.Contracts.Monitoring.DeviceChangeRequests;

public sealed record ReviewDeviceChangeRequestRequest(string? ReviewComment);
```

- [ ] **Step 2: Write the failing controller test**

```csharp
[Fact]
public async Task Approvals_WithoutApprovePermission_Returns403()
{
    var client = Factory.CreateClientAsUser(withPermission: null);

    var response = await client.GetAsync("/api/v1/monitoring/device-change-requests/approvals");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task Approve_WithApprovePermission_ReturnsOkAndDeactivatesOldDevice()
{
    var pending = await SeedPendingDeviceChangeRequestAsync();
    var client = Factory.CreateClientAsUser(withPermission: "attendance:approve", asUserId: pending.ResolvedApproverUserId);

    var response = await client.PostAsJsonAsync(
        $"/api/v1/monitoring/device-change-requests/{pending.Request.Id}/approve",
        new ReviewDeviceChangeRequestRequest(null));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

(Match the exact fixture/factory helper names this repo's existing
`LocationChangeRequestsController` HTTP tests use — copy their setup
pattern rather than inventing new helper names.)

Run the new test file. Expected: FAIL (controller doesn't exist).

- [ ] **Step 3: Write the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Monitoring.DeviceChangeRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.Monitoring;

/// <summary>Web/admin-facing endpoints for reviewing an employee's device-change request.
/// Requests are never submitted through this controller - they're raised automatically
/// by TrayEnrollmentService when a mismatch is detected. See the device-change-approval
/// design spec.</summary>
[ApiController]
[Route("api/v1/monitoring/device-change-requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class DeviceChangeRequestsController(IMediator mediator) : ControllerBase
{
    [HttpGet("approvals")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approvals([FromQuery] PagedRequest paging, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListDeviceChangeRequestApprovalsQuery(paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ReviewDeviceChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveDeviceChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] ReviewDeviceChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectDeviceChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Api/Contracts/Monitoring/DeviceChangeRequests HRMS-Backend-v1/src/ONEVO.Api/Controllers/Tenant/Monitoring/DeviceChangeRequestsController.cs HRMS-Backend-v1/tests/ONEVO.Api.Tests/Controllers/Tenant/Monitoring/DeviceChangeRequestsControllerTests.cs
git commit -m "feat(backend): expose device change request approvals endpoints"
```

---

### Task 7: Backend end-to-end integration test

**Files:**
- Test: `HRMS-Backend-v1/tests/ONEVO.IntegrationTests/Monitoring/DeviceChangeApprovalFlowTests.cs` (mirror whichever existing file drives a full poll/exchange flow against a real test database, e.g. the tray-browser-auto-connect integration test referenced in memory)

- [ ] **Step 1: Write the end-to-end test**

```csharp
[Fact]
public async Task DeviceChangeFlow_BlockApproveRetry_EndsWithNewDeviceActiveAndOldRevoked()
{
    // 1. Enroll device A (first enrollment) - succeeds.
    var deviceAResult = await ExchangeActivationCodeAsync(employeeId: Employee.Id, fingerprint: "fp-A");
    Assert.True(deviceAResult.IsSuccess);

    // 2. Attempt to enroll device B for the same employee - blocked.
    var deviceBAttempt = await ExchangeActivationCodeAsync(employeeId: Employee.Id, fingerprint: "fp-B");
    Assert.False(deviceBAttempt.IsSuccess);
    Assert.Equal("device_change_pending", deviceBAttempt.ErrorCode);

    var registrations = await CountActiveDeviceRegistrationsAsync(Employee.Id);
    Assert.Equal(1, registrations); // still only device A

    // 3. Approve the raised request as the resolved approver.
    var pendingRequest = await GetPendingDeviceChangeRequestAsync(Employee.Id);
    var approveResult = await ApproveDeviceChangeRequestAsync(pendingRequest.Id, approverUserId: Approver.Id);
    Assert.True(approveResult.IsSuccess);

    // 4. Retry enrolling device B - now succeeds; device A is inactive.
    var deviceBRetry = await ExchangeActivationCodeAsync(employeeId: Employee.Id, fingerprint: "fp-B");
    Assert.True(deviceBRetry.IsSuccess);

    var deviceAStillActive = await IsDeviceActiveAsync(fingerprint: "fp-A");
    Assert.False(deviceAStillActive);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.IntegrationTests --filter DeviceChangeApprovalFlowTests`
Expected: PASS once Tasks 1-6 are complete (this test is written last, as a
regression net over the whole flow — if any earlier task's tests already
cover a step, that's fine, this one checks the wiring between them).

- [ ] **Step 2: Commit**

```bash
git add HRMS-Backend-v1/tests/ONEVO.IntegrationTests/Monitoring/DeviceChangeApprovalFlowTests.cs
git commit -m "test(backend): add end-to-end device change approval flow test"
```

---

### Task 8: Frontend — `DeviceChangeApprovalsComponent` (list, approve, reject)

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/device-change-approvals/device-change-approvals.component.ts` (+ `.html`, `.css`, `.spec.ts`)
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/data-access/device-change-requests.store.ts`
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/data-access/device-change-request-api.service.ts`

**Interfaces:**
- Consumes: `GET /api/v1/monitoring/device-change-requests/approvals`, `POST /api/v1/monitoring/device-change-requests/{id}/approve`, `POST /api/v1/monitoring/device-change-requests/{id}/reject` (Task 6).
- Produces: `DeviceChangeApprovalsComponent` — consumed by Task 9.

- [ ] **Step 1: Read the existing pattern first**

Open `location-change-request-api.service.ts`, `LocationChangeRequestsStore`
(the file `time-tracking.component.ts:14` imports), and
`LocationChangeApprovalsComponent`'s `.ts`/`.html`/`.spec.ts`. Copy their
exact structure (HTTP client injection style, signal/store shape, list +
approve/reject action methods, loading/error state handling, spec test
shape) — this task is a same-pattern mirror, not a new design.

- [ ] **Step 2: Write the failing spec test**

Mirror `location-change-approvals.component.spec.ts`'s test cases
one-for-one, renamed to device-change terms: renders the pending list from
the store, calls the store's approve method with the request id on Approve
click, calls reject with a required comment on Reject.

Run the new spec. Expected: FAIL (component/store/service don't exist).

- [ ] **Step 3: Implement the API service, store, and component**

Structure exactly mirrors `location-change-request-api.service.ts` →
`LocationChangeRequestsStore` → `LocationChangeApprovalsComponent`, with:
- API service methods: `listApprovals(paging)`, `approve(id, reviewComment)`, `reject(id, reviewComment)` hitting the three endpoints above.
- Store: signals for `items`, `loading`, `error`, `totalCount`; `loadApprovals()`, `approve(id, comment)`, `reject(id, comment)` methods that call the API service and refresh the list on success.
- Component template: table/list of `{ requesterDisplayName, newDeviceName, newDeviceOs, requestedAt }` rows with Approve/Reject buttons, following the same visual layout as `location-change-approvals.component.html`.

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/device-change-approvals Hrms--Web-application---front-end---v1/src/app/modules/attendance/data-access/device-change-requests.store.ts Hrms--Web-application---front-end---v1/src/app/modules/attendance/data-access/device-change-request-api.service.ts
git commit -m "feat(frontend): add device change approvals component"
```

---

### Task 9: Wire the new tab into the Requests screen

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/time-tracking/time-tracking.component.ts:11-14,94-97`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/time-tracking/time-tracking.component.html` (tab button list)
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/time-tracking/time-tracking.component.spec.ts`

**Interfaces:**
- Consumes: `DeviceChangeApprovalsComponent` (Task 8).

- [ ] **Step 1: Write the failing test**

```typescript
it('shows the device-change tab alongside corrections, work-area, and location', () => {
  // follow the existing spec's pattern for asserting a tab is present/selectable
  expect(component.approvalType()).toBeDefined();
  component.selectApprovalType('device-change');
  expect(component.approvalType()).toBe('device-change');
});
```

(Match this repo's actual existing test style for tab switching in this
spec file — copy the assertion pattern used for the `'location'` tab.)

Run the spec. Expected: FAIL (`'device-change'` isn't a valid `approvalType` yet).

- [ ] **Step 2: Add the fourth tab**

```typescript
// time-tracking.component.ts:94
readonly approvalType = computed<'corrections' | 'work-area' | 'location' | 'device-change'>(...)
```

Import `DeviceChangeApprovalsComponent` alongside the other three at line
11-13, add it to the component's `imports` array, and add a fourth tab
button/panel in the template following the exact markup pattern the
`'location'` tab already uses (same conditional rendering, same button
group).

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Hrms--Web-application---front-end---v1/src/app/modules/attendance/feature/time-tracking
git commit -m "feat(frontend): add device-change tab to the Requests screen"
```

---

### Task 10: TrayApp — error code mapping in `OnevoApiClient`

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs:~98` (poll-result mapping) and the manual activation-code exchange result mapping in the same file
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs` (extend existing)

**Interfaces:**
- Produces: a `DEVICE_CHANGE_PENDING` (or equivalent) result state — consumed by Task 11.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task PollDeviceAuthorizationAsync_WhenServerReturnsDeviceChangePending_MapsToDeviceChangePendingState()
{
    Http.RespondWith(HttpStatusCode.Conflict, new { error_code = "device_change_pending" });

    var result = await Client.PollDeviceAuthorizationAsync(DeviceCode, Fingerprint, CancellationToken.None);

    Assert.Equal(DeviceAuthorizationPollState.DeviceChangePending, result.State);
}
```

Run: `dotnet test HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests --filter OnevoApiClientTests`
Expected: FAIL (`DeviceAuthorizationPollState.DeviceChangePending` doesn't exist).

- [ ] **Step 2: Add the new state and mapping**

Add `DeviceChangePending` to the `DeviceAuthorizationPollState` enum, then
add a case beside the existing ones at `OnevoApiClient.cs:98-101`:

```csharp
"device_change_pending" => new(DeviceAuthorizationPollState.DeviceChangePending, null),
```

Apply the equivalent addition to whichever result type the manual
activation-code exchange path uses (the type `ConnectWorkspaceViewModel`'s
`VerifyAndConnectAsync` reads `result.ErrorCode` from) — add a passthrough
so `"device_change_pending"` (lowercase, from the API) surfaces as
`"DEVICE_CHANGE_PENDING"` (the uppercase convention that path's other codes
use, e.g. `"INVALID_CODE"`, `"ALREADY_ENROLLED"`).

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs
git commit -m "feat(tray): map device_change_pending in both enrollment paths"
```

---

### Task 11: TrayApp — friendly message in `ConnectWorkspaceViewModel`

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs:92-99,197-203`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs` (extend existing)

**Interfaces:**
- Consumes: `"DEVICE_CHANGE_PENDING"` error code (Task 10).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task VerifyAndConnectAsync_WhenDeviceChangePending_ShowsApprovalMessage()
{
    Pipe.SendActivationAsyncResult = new ActivationResult(Success: false, ErrorCode: "DEVICE_CHANGE_PENDING");

    await ViewModel.VerifyAndConnectCommand.ExecuteAsync(null);

    Assert.Contains("device-change request", ViewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task HandleDevicePairingResult_WhenDeviceChangePending_ShowsApprovalMessage()
{
    ViewModel.InvokeHandleDevicePairingResult(new DevicePairingResultPayload(Success: false, ErrorCode: "DEVICE_CHANGE_PENDING"));

    Assert.Contains("device-change request", ViewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
}
```

(Match the existing test file's actual helper/invocation names for
triggering these two code paths — copy the pattern used for the existing
`"ACCESS_DENIED"`/`"INVALID_CODE"` test cases.)

Run: `dotnet test HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests --filter ConnectWorkspaceViewModelTests`
Expected: FAIL.

- [ ] **Step 2: Add the mapping in both switch expressions**

`VerifyAndConnectAsync` (line ~92):

```csharp
ErrorMessage = result.ErrorCode switch
{
    "INVALID_CODE" => "Invalid or expired code. Generate a new code in the web portal.",
    "LOCKED" => "The tray is locked. Restart the ONEVO service and try again.",
    "ALREADY_ENROLLED" => "This tray is already connected. Use the existing connected session.",
    "SERVICE_UNAVAILABLE" => "Can't reach the ONEVO backend right now. Check your connection and try again.",
    "DEVICE_CHANGE_PENDING" => "A different device is already approved for your account. We've sent a device-change request to your approver — you'll be notified once it's approved.",
    _ => result.ErrorCode ?? "Activation failed."
};
```

`HandleDevicePairingResult` (line ~197):

```csharp
ErrorMessage = result.ErrorCode switch
{
    "ACCESS_DENIED" => "Request denied in the browser.",
    "EXPIRED" => "The browser request expired — try again.",
    "SERVICE_UNAVAILABLE" => "Can't reach the ONEVO backend right now. Check your connection and try again.",
    "DEVICE_CHANGE_PENDING" => "A different device is already approved for your account. We've sent a device-change request to your approver — you'll be notified once it's approved.",
    _ => result.ErrorCode ?? "Browser connect failed."
};
```

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs
git commit -m "feat(tray): show a device-change-pending message on both connect paths"
```
