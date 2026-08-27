# Monitoring Consent Gate (WFM-11) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded `SetConsentValid(true)` stub with a real, backend-recorded, versioned monitoring consent that gates `ClockIn`/`EndBreak` via the existing `LifecycleGate`.

**Architecture:** Backend gets a new `EmployeeMonitoringConsent` entity plus consent fields on the existing tray-policy response (no new poll loop — piggybacks on `PolicySyncService`'s existing cadence) and a new accept endpoint. The Service applies the fetched `HasValidConsent` to `LifecycleGate`, and returns a specific `CONSENT_REQUIRED` error code (instead of generic `GATES_CLOSED`) so the TrayApp can route to the existing `PrivacyConsentPage`. The TrayApp records real acceptance over a new IPC round trip instead of a no-op button.

**Tech Stack:** ASP.NET Core / EF Core / PostgreSQL (backend, xUnit+Moq+FluentAssertions), .NET 10 Windows Service + MAUI (agent, xUnit+plain Assert).

**Spec:** `docs/superpowers/specs/2026-08-24-monitoring-consent-gate-design.md`

**Repos involved (separate git repos — commit each independently):**
- Backend: `C:\HR\HRMS-Backend-v1`
- Agent: `C:\HR\tray_app_maui`

---

## Part A — Backend (`C:\HR\HRMS-Backend-v1`)

### Task 1: `EmployeeMonitoringConsent` entity, EF config, migration, repository, DI

No dedicated unit test for this task — it's schema/plumbing with no branching logic. It's exercised indirectly by Task 3/4 tests. Verify with a build instead.

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/Consent/Entities/EmployeeMonitoringConsent.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Consent/EmployeeMonitoringConsentConfiguration.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Consent/RepositoryInterfaces/IMonitoringConsentRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Consent/EfMonitoringConsentRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: EF migration (generated, then hand-edited for RLS)

- [ ] **Step 1: Create the domain entity**

```csharp
// src/ONEVO.Domain/Features/Monitoring/Consent/Entities/EmployeeMonitoringConsent.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Consent.Entities;

/// <summary>
/// Immutable acceptance record — one row per accept, never updated or deleted. "Current" status is
/// always the latest row by AcceptedAt for a given (tenant, employee); this gives a full audit
/// trail instead of overwriting history, which matters for a legally-sensitive consent record.
/// </summary>
public class EmployeeMonitoringConsent : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ConsentTextVersion { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Consent/EmployeeMonitoringConsentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Consent.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Consent;

public class EmployeeMonitoringConsentConfiguration : IEntityTypeConfiguration<EmployeeMonitoringConsent>
{
    public void Configure(EntityTypeBuilder<EmployeeMonitoringConsent> builder)
    {
        builder.ToTable("employee_monitoring_consents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ConsentTextVersion).HasMaxLength(50).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.AcceptedAt });
    }
}
```

- [ ] **Step 3: Register the DbSet**

Add next to the existing `EmployeeCheckIns` DbSet in `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (near line 99):

```csharp
    public DbSet<ONEVO.Domain.Features.Monitoring.Consent.Entities.EmployeeMonitoringConsent> EmployeeMonitoringConsents
        => Set<ONEVO.Domain.Features.Monitoring.Consent.Entities.EmployeeMonitoringConsent>();
```

(Use a `using ONEVO.Domain.Features.Monitoring.Consent.Entities;` at the top of the file instead of the fully-qualified name if the file's existing using-block groups by feature — match whatever the surrounding lines already do.)

- [ ] **Step 4: Repository interface**

```csharp
// src/ONEVO.Application/Features/Monitoring/Consent/RepositoryInterfaces/IMonitoringConsentRepository.cs
using ONEVO.Domain.Features.Monitoring.Consent.Entities;

namespace ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces;

public interface IMonitoringConsentRepository
{
    /// <summary>Latest acceptance on file for this employee, or null if they've never accepted anything.</summary>
    Task<EmployeeMonitoringConsent?> GetLatestAsync(Guid tenantId, Guid employeeId, CancellationToken ct);

    Task AddAsync(EmployeeMonitoringConsent consent, CancellationToken ct);
}
```

- [ ] **Step 5: EF repository implementation**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Consent/EfMonitoringConsentRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Consent.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Consent;

public class EfMonitoringConsentRepository : IMonitoringConsentRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitoringConsentRepository(ApplicationDbContext db) => _db = db;

    public async Task<EmployeeMonitoringConsent?> GetLatestAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
        => await _db.EmployeeMonitoringConsents
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .OrderByDescending(c => c.AcceptedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(EmployeeMonitoringConsent consent, CancellationToken ct)
        => await _db.EmployeeMonitoringConsents.AddAsync(consent, ct);
}
```

- [ ] **Step 6: DI registration**

Add next to the existing `ICheckInRepository` line in `src/ONEVO.Infrastructure/DependencyInjection.cs` (near line 432):

```csharp
        services.AddScoped<IMonitoringConsentRepository, EfMonitoringConsentRepository>();
```

(Add the two matching `using` statements for `ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces` and `ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Consent` at the top of the file if not already covered by a wildcard-style grouping.)

- [ ] **Step 7: Verify it builds**

Run: `dotnet build src/ONEVO.Infrastructure --no-restore` (from `C:\HR\HRMS-Backend-v1`)
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Generate the migration**

Run (from `C:\HR\HRMS-Backend-v1`):
```bash
dotnet ef migrations add AddEmployeeMonitoringConsent --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: creates `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddEmployeeMonitoringConsent.cs` and `.Designer.cs`, and updates `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 9: Add RLS policy in the same migration**

Open the generated `<timestamp>_AddEmployeeMonitoringConsent.cs`. After the `migrationBuilder.CreateTable(...)` call inside `Up`, append (matching the exact pattern in `20260719180411_AddMissingRlsPolicies.cs`):

```csharp
            migrationBuilder.Sql(@"
                ALTER TABLE employee_monitoring_consents ENABLE ROW LEVEL SECURITY;
                ALTER TABLE employee_monitoring_consents FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON employee_monitoring_consents;
                CREATE POLICY tenant_isolation ON employee_monitoring_consents
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
```

And in `Down`, **before** the generated `migrationBuilder.DropTable(...)` call, add:

```csharp
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON employee_monitoring_consents;
                ALTER TABLE employee_monitoring_consents DISABLE ROW LEVEL SECURITY;
            ");
```

This is the exact gap called out in `[[project_onevo_hrms]]`'s "System-Mode RLS Gap" memory — a new tenant table with no RLS silently returns zero rows for tenant-mode queries and is easy to forget as a separate followup, so it's folded into this same migration instead.

- [ ] **Step 10: Apply the migration locally and verify**

Run: `ops/postgres/setup-local-db.ps1 -RunMigrations` (from `C:\HR\HRMS-Backend-v1`, per `[[project_hrms_migration_drift_2026-08-20]]` — do not use bare `dotnet ef database update`, it missed migrations before).
Expected: migration applies with no errors; `employee_monitoring_consents` table exists with RLS enabled.

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/Consent src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Consent src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Consent src/ONEVO.Application/Features/Monitoring/Consent/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations
git commit -m "feat(monitoring): add EmployeeMonitoringConsent entity, repository, and RLS-protected table"
```

---

### Task 2: Consent text constant + extend `TrayAgentPolicyDto`

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Consent/MonitoringConsentText.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs`

- [ ] **Step 1: Create the consent text constant**

```csharp
// src/ONEVO.Application/Features/Monitoring/Consent/MonitoringConsentText.cs
namespace ONEVO.Application.Features.Monitoring.Consent;

/// <summary>
/// Fixed default monitoring disclosure for R1 — not admin-editable per tenant yet (Phase 2).
/// Bump CurrentVersion whenever the wording changes materially; every employee who accepted an
/// older version is re-gated to the consent screen on their next ClockIn/EndBreak attempt.
/// </summary>
public static class MonitoringConsentText
{
    public const string CurrentVersion = "1";

    public const string Text =
        "While you are clocked in, ONEVO WorkPulse Agent collects: keyboard and mouse activity " +
        "counts (not the keys you press or type content), the name of the application in focus, " +
        "and idle/active time. A face scan is used to verify your identity at clock-in. This data " +
        "is used only for attendance and productivity reporting to your employer. You will not be " +
        "monitored while clocked out, on an approved break, or on approved time off.";
}
```

- [ ] **Step 2: Extend the DTO**

Modify `src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Policy.DTOs;

public sealed record TrayAgentPolicyDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("consent_text_version")] string ConsentTextVersion,
    [property: JsonPropertyName("consent_text")] string ConsentText,
    [property: JsonPropertyName("has_valid_consent")] bool HasValidConsent);
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/ONEVO.Application --no-restore` (from `C:\HR\HRMS-Backend-v1`)
Expected: FAILS — `GetEffectiveTrayPolicyQueryHandler.cs` constructs a `TrayAgentPolicyDto` positionally and is now missing 3 required arguments. This is expected; Task 3 fixes it.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Consent/MonitoringConsentText.cs src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs
git commit -m "feat(monitoring): add fixed consent disclosure text and extend TrayAgentPolicyDto"
```

---

### Task 3: `GetEffectiveTrayPolicyQueryHandler` computes `HasValidConsent`

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/Policy/Queries/GetEffectiveTrayPolicy/GetEffectiveTrayPolicyQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Policy/GetEffectiveTrayPolicyQueryHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `GetEffectiveTrayPolicyQueryHandlerTests.cs` — first add the new mock field and constructor argument (`private readonly Mock<IMonitoringConsentRepository> _consents = new();`, passed into `CreateSut()`), plus the two `using` statements (`ONEVO.Application.Features.Monitoring.Consent`, `ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces`, `ONEVO.Domain.Features.Monitoring.Consent.Entities`), then add:

```csharp
    [Fact]
    public async Task No_consent_record_returns_HasValidConsent_false()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, false);
        Set(MonitoringCapability.AutoScreenshotCapture, false);
        Set(MonitoringCapability.IdentityVerification, false);
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeMonitoringConsent?)null);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasValidConsent.Should().BeFalse();
        result.Value.ConsentTextVersion.Should().Be(MonitoringConsentText.CurrentVersion);
        result.Value.ConsentText.Should().Be(MonitoringConsentText.Text);
    }

    [Fact]
    public async Task Consent_at_current_version_returns_HasValidConsent_true()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, false);
        Set(MonitoringCapability.AutoScreenshotCapture, false);
        Set(MonitoringCapability.IdentityVerification, false);
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeMonitoringConsent
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EmployeeId = _userId,
                ConsentTextVersion = MonitoringConsentText.CurrentVersion,
                AcceptedAt = _clock.UtcNow.AddDays(-1)
            });

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.Value!.HasValidConsent.Should().BeTrue();
    }

    [Fact]
    public async Task Consent_at_stale_version_returns_HasValidConsent_false()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, false);
        Set(MonitoringCapability.AutoScreenshotCapture, false);
        Set(MonitoringCapability.IdentityVerification, false);
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeMonitoringConsent
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EmployeeId = _userId,
                ConsentTextVersion = "0-superseded",
                AcceptedAt = _clock.UtcNow.AddDays(-1)
            });

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.Value!.HasValidConsent.Should().BeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "GetEffectiveTrayPolicyQueryHandlerTests" --no-build` (from `C:\HR\HRMS-Backend-v1`) — this will fail to even compile first (handler constructor doesn't accept `IMonitoringConsentRepository` yet, DTO still missing the 3 args from Task 2). That compile failure is the expected "red" for this step.

- [ ] **Step 3: Implement**

Modify `GetEffectiveTrayPolicyQueryHandler.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Consent;
using ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;

namespace ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

public sealed class GetEffectiveTrayPolicyQueryHandler
    : IRequestHandler<GetEffectiveTrayPolicyQuery, Result<TrayAgentPolicyDto>>
{
    internal static readonly TimeSpan PolicyValidity = TimeSpan.FromHours(1);

    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringToggleResolver _toggles;
    private readonly IMonitoringConsentRepository _consents;
    private readonly IDateTimeProvider _clock;

    public GetEffectiveTrayPolicyQueryHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringToggleResolver toggles,
        IMonitoringConsentRepository consents,
        IDateTimeProvider clock)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _toggles = toggles;
        _consents = consents;
        _clock = clock;
    }

    public async Task<Result<TrayAgentPolicyDto>> Handle(
        GetEffectiveTrayPolicyQuery request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<TrayAgentPolicyDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<TrayAgentPolicyDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;

        var activityEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ActivityMonitoring, cancellationToken);
        var appUsageEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ApplicationTracking, cancellationToken);
        var screenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ScreenshotCapture, cancellationToken);
        var autoScreenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.AutoScreenshotCapture, cancellationToken);
        var cameraEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.IdentityVerification, cancellationToken);
        var idleThresholdMinutes = await _toggles.GetIdleThresholdMinutesAsync(
            tenantId, employeeId, cancellationToken);

        var inactivityEnabled = activityEnabled && screenshotEnabled && autoScreenshotEnabled;
        var now = _clock.UtcNow;

        var latestConsent = await _consents.GetLatestAsync(tenantId, employeeId, cancellationToken);
        var hasValidConsent = latestConsent is not null
            && latestConsent.ConsentTextVersion == MonitoringConsentText.CurrentVersion;

        return Result<TrayAgentPolicyDto>.Success(new TrayAgentPolicyDto(
            ComputeVersion(activityEnabled, appUsageEnabled, screenshotEnabled, autoScreenshotEnabled, cameraEnabled, idleThresholdMinutes),
            activityEnabled,
            appUsageEnabled,
            screenshotEnabled,
            inactivityEnabled,
            cameraEnabled,
            idleThresholdMinutes,
            now.Add(PolicyValidity),
            MonitoringConsentText.CurrentVersion,
            MonitoringConsentText.Text,
            hasValidConsent));
    }

    internal static string ComputeVersion(
        bool activityEnabled,
        bool appUsageEnabled,
        bool screenshotEnabled,
        bool autoScreenshotEnabled,
        bool cameraEnabled,
        int idleThresholdMinutes)
    {
        var fingerprint =
            $"{activityEnabled}:{appUsageEnabled}:{screenshotEnabled}:{autoScreenshotEnabled}:{cameraEnabled}:{idleThresholdMinutes}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "GetEffectiveTrayPolicyQueryHandlerTests" --no-build`
Expected: PASS, all 8 tests (5 existing + 3 new) green.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Policy tests/ONEVO.Tests.Unit/Features/Monitoring/Policy
git commit -m "feat(monitoring): compute HasValidConsent in the effective tray policy"
```

---

### Task 4: Accept-consent command + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Consent/Commands/AcceptMonitoringConsent/AcceptMonitoringConsentCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Consent/Commands/AcceptMonitoringConsent/AcceptMonitoringConsentCommandHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Consent/TrayMonitoringConsentController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Consent/AcceptMonitoringConsentCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Monitoring/Consent/AcceptMonitoringConsentCommandHandlerTests.cs
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Consent.Commands.AcceptMonitoringConsent;
using ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Consent.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Consent;

public class AcceptMonitoringConsentCommandHandlerTests
{
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<IMonitoringConsentRepository> _consents = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly FrozenClock _clock = new(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public AcceptMonitoringConsentCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });
    }

    private AcceptMonitoringConsentCommandHandler CreateSut() => new(
        _device.Object, _tenants.Object, _switcher.Object, _consents.Object, _clock, _unitOfWork.Object);

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new AcceptMonitoringConsentCommand("1"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Records_a_new_consent_row_with_the_submitted_version()
    {
        EmployeeMonitoringConsent? added = null;
        _consents.Setup(c => c.AddAsync(It.IsAny<EmployeeMonitoringConsent>(), It.IsAny<CancellationToken>()))
            .Callback<EmployeeMonitoringConsent, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(new AcceptMonitoringConsentCommand("1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(_tenantId);
        added.EmployeeId.Should().Be(_userId);
        added.ConsentTextVersion.Should().Be("1");
        added.AcceptedAt.Should().Be(_clock.UtcNow);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FrozenClock : IDateTimeProvider
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "AcceptMonitoringConsentCommandHandlerTests" --no-build`
Expected: FAIL to compile — `AcceptMonitoringConsentCommand`/`AcceptMonitoringConsentCommandHandler` don't exist yet.

- [ ] **Step 3: Implement the command and handler**

```csharp
// src/ONEVO.Application/Features/Monitoring/Consent/Commands/AcceptMonitoringConsent/AcceptMonitoringConsentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Consent.Commands.AcceptMonitoringConsent;

public record AcceptMonitoringConsentCommand(string ConsentTextVersion) : IRequest<Result<bool>>;
```

```csharp
// src/ONEVO.Application/Features/Monitoring/Consent/Commands/AcceptMonitoringConsent/AcceptMonitoringConsentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Consent.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Consent.Entities;

namespace ONEVO.Application.Features.Monitoring.Consent.Commands.AcceptMonitoringConsent;

public class AcceptMonitoringConsentCommandHandler
    : IRequestHandler<AcceptMonitoringConsentCommand, Result<bool>>
{
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringConsentRepository _consents;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public AcceptMonitoringConsentCommandHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringConsentRepository consents,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _consents = consents;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(AcceptMonitoringConsentCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<bool>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<bool>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        await _consents.AddAsync(new EmployeeMonitoringConsent
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = _device.UserId,
            ConsentTextVersion = request.ConsentTextVersion,
            AcceptedAt = _clock.UtcNow
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "AcceptMonitoringConsentCommandHandlerTests" --no-build`
Expected: PASS, 2/2 tests green.

- [ ] **Step 5: Add the controller endpoint**

```csharp
// src/ONEVO.Api/Controllers/Tenant/Monitoring/Consent/TrayMonitoringConsentController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Consent.Commands.AcceptMonitoringConsent;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Consent;

/// <summary>
/// Monitoring consent acceptance for the authenticated tray device.
/// Authorization: Bearer {tray_access_token}
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayMonitoringConsentController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrayMonitoringConsentController(IMediator mediator)
        => _mediator = mediator;

    public sealed record AcceptConsentRequest(string ConsentTextVersion);

    /// <summary>
    /// Records the authenticated employee's acceptance of the current monitoring disclosure.
    /// Tenant and employee identity come only from the tray JWT — never from the request body.
    /// </summary>
    [HttpPost("consent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AcceptConsent([FromBody] AcceptConsentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcceptMonitoringConsentCommand(request.ConsentTextVersion), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok();
    }
}
```

- [ ] **Step 6: Full backend build + test run**

There is no top-level `.sln`/`.slnx` in this repo — build and test per-project. Run (from `C:\HR\HRMS-Backend-v1`): `dotnet build src/ONEVO.Api` then `dotnet test tests/ONEVO.Tests.Unit`
Expected: build succeeds (pre-existing warnings in unrelated files are normal — this repo does not have `TreatWarningsAsErrors` on); test run reports `Failed: 0` (baseline on this branch is 3110 passed, 0 failed — your new tests add to that count).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Consent/Commands src/ONEVO.Api/Controllers/Tenant/Monitoring/Consent tests/ONEVO.Tests.Unit/Features/Monitoring/Consent
git commit -m "feat(monitoring): add POST /api/v1/monitoring/tray/consent accept endpoint"
```

---

## Part B — Shared (`C:\HR\tray_app_maui\ONEVO.Agent.Shared`)

### Task 5: Extend `AgentPolicy` + new IPC message types

**Files:**
- Modify: `ONEVO.Agent.Shared/Models/AgentPolicy.cs`
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- Test: `tests/ONEVO.Agent.Shared.Tests/Models/AgentPolicyTests.cs` (create if it doesn't already exist as a file covering this record)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Agent.Shared.Tests/Models/AgentPolicyTests.cs
namespace ONEVO.Agent.Shared.Tests.Models;

using ONEVO.Agent.Shared.Models;
using Xunit;

public sealed class AgentPolicyTests
{
    [Fact]
    public void ConsentFields_default_to_unconsented()
    {
        var policy = new AgentPolicy { Version = "1" };

        Assert.Equal(string.Empty, policy.ConsentTextVersion);
        Assert.Equal(string.Empty, policy.ConsentText);
        Assert.False(policy.HasValidConsent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Shared.Tests --filter "AgentPolicyTests" --no-build` (from `C:\HR\tray_app_maui`)
Expected: FAIL to compile — `ConsentTextVersion`/`ConsentText`/`HasValidConsent` don't exist on `AgentPolicy` yet.

- [ ] **Step 3: Extend `AgentPolicy`**

```csharp
// ONEVO.Agent.Shared/Models/AgentPolicy.cs
namespace ONEVO.Agent.Shared.Models;

public sealed record AgentPolicy
{
    public required string Version { get; init; }
    public bool ActivitySignalEnabled { get; init; }
    public bool AppUsageEnabled { get; init; }
    public bool ScreenshotEnabled { get; init; }
    public bool CameraVerificationEnabled { get; init; }
    public bool InactivityScreenshotEnabled { get; init; }

    /// <summary>
    /// Minutes of continuous mouse/keyboard inactivity before the "Activity check" screenshot
    /// prompt fires. Defaults to 5 so every existing test/local-default fixture that constructs
    /// an AgentPolicy without setting this explicitly keeps a sane, non-zero value (0 would mean
    /// "prompt on every poll tick", which is not a safe default for anything).
    /// </summary>
    public int IdleThresholdMinutes { get; init; } = 5;

    public DateTimeOffset ValidUntil { get; init; }

    /// <summary>Current tenant consent disclosure version — bumps only when the wording changes.</summary>
    public string ConsentTextVersion { get; init; } = string.Empty;

    /// <summary>Full disclosure text to show on PrivacyConsentPage.</summary>
    public string ConsentText { get; init; } = string.Empty;

    /// <summary>True if this employee's latest recorded consent matches ConsentTextVersion.</summary>
    public bool HasValidConsent { get; init; }
}
```

- [ ] **Step 4: Add the IPC message types and payloads**

Add to `ONEVO.Agent.Shared/IPC/IpcMessages.cs`, inside `IpcMessageTypes` (after `BiometricEnrollmentResult`):

```csharp
    /// <summary>Tray → Service: employee accepted the monitoring consent disclosure at this version.</summary>
    public const string ConsentAcceptSubmit = "ConsentAcceptSubmit";

    /// <summary>Service → Tray: result of recording the consent acceptance.</summary>
    public const string ConsentAcceptResult = "ConsentAcceptResult";
```

And after the existing `BiometricEnrollmentResultPayload` record at the bottom of the file:

```csharp
public sealed record ConsentAcceptSubmitPayload(string ConsentTextVersion);

public sealed record ConsentAcceptResultPayload(bool Success, string? ErrorCode);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Shared.Tests --filter "AgentPolicyTests" --no-build`
Expected: PASS.

- [ ] **Step 6: Full Shared build + test run**

Run: `dotnet build ONEVO.Agent.Shared --no-restore` then `dotnet test tests/ONEVO.Agent.Shared.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests green.

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.Shared/Models/AgentPolicy.cs ONEVO.Agent.Shared/IPC/IpcMessages.cs tests/ONEVO.Agent.Shared.Tests/Models/AgentPolicyTests.cs
git commit -m "feat(shared): add consent fields to AgentPolicy and ConsentAccept IPC messages"
```

---

## Part C — Service (`C:\HR\tray_app_maui\ONEVO.Agent.Service`)

### Task 6: `LifecycleGate` returns a specific blocked-reason code

**Files:**
- Modify: `ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Lifecycle/LifecycleGateTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `LifecycleGateTests.cs`:

```csharp
    [Fact]
    public void GetBlockedReasonCode_WhenConsentMissing_ReturnsConsentRequired()
    {
        var gate = BuildFullyOpen();
        gate.SetConsentValid(false);
        Assert.Equal("CONSENT_REQUIRED", gate.GetBlockedReasonCode());
    }

    [Fact]
    public void GetBlockedReasonCode_WhenSomeOtherGateFails_ReturnsGatesClosed()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnBreak(false);
        Assert.Equal("GATES_CLOSED", gate.GetBlockedReasonCode());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "LifecycleGateTests" --no-build` (from `C:\HR\tray_app_maui`)
Expected: FAIL to compile — `GetBlockedReasonCode` doesn't exist yet.

- [ ] **Step 3: Implement**

Add to `LifecycleGate.cs`, after `Snapshot()`:

```csharp
    /// <summary>
    /// Call only when <see cref="CanActivate"/> is false, to get a specific reason for the caller
    /// to act on. Consent is checked first and reported specifically (so the caller can route to
    /// the consent screen) — every other single- or multi-gate failure reports the generic
    /// "GATES_CLOSED", matching prior behavior for those cases.
    /// </summary>
    public string GetBlockedReasonCode()
    {
        lock (_lock)
        {
            if (!_consentValid)
                return "CONSENT_REQUIRED";
            return "GATES_CLOSED";
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "LifecycleGateTests" --no-build`
Expected: PASS, all tests green (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs tests/ONEVO.Agent.Service.Tests/Lifecycle/LifecycleGateTests.cs
git commit -m "feat(agent): LifecycleGate reports CONSENT_REQUIRED specifically when consent is the blocker"
```

---

### Task 7: `AgentWorker` uses the specific reason code; remove the consent stub

**Files:**
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`

No new dedicated test for this task — `AgentWorker` has no existing unit test seam (13 constructor dependencies, previously exercised only via the `run` skill's live stack, not unit tests) and Task 6 already covers the branching logic being called. This step is a small, low-risk substitution verified by the full Service test suite staying green plus a manual live check in Task 9.

- [ ] **Step 1: Replace the two `GATES_CLOSED` literals**

In `ExecuteClockIn` (near line 356), change:
```csharp
            return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);
```
to:
```csharp
            return (false, _lifecycleGate.GetBlockedReasonCode(), "Monitoring gates are not satisfied.", current);
```

In `ExecuteEndBreak` (near line 394), change:
```csharp
            return (false, "GATES_CLOSED", "Cannot resume — gates not satisfied.", current);
```
to:
```csharp
            return (false, _lifecycleGate.GetBlockedReasonCode(), "Cannot resume — gates not satisfied.", current);
```

- [ ] **Step 2: Remove the hardcoded consent stub**

In `ApplyEnrollmentGates()` (near line 125), delete the `SetConsentValid(true)` line and its comment, and update the remaining comment to reflect only the still-true gap:

```csharp
    private void ApplyEnrollmentGates()
    {
        _lifecycleGate.SetDeviceEnrolled(true);
        _lifecycleGate.SetCredentialValid(true);
        _lifecycleGate.SetDeviceApproved(true);

        // Server policy-fetch determines consent (PolicySyncService sets SetConsentValid from
        // the fetched policy's HasValidConsent — see Task 8). Employee session / policy-allows /
        // not-on-time-off capture is still not built (§23 gap) — until it exists, a successful
        // backend-verified login is the strongest signal we have, so these stay true post-
        // enrollment. Replace with real sources once those features land; do not silently
        // regress Clock In in the meantime by leaving them false.
        _lifecycleGate.SetEmployeeSessionActive(true);
        _lifecycleGate.SetPolicyAllowsCollection(true);
        _lifecycleGate.SetNotOnApprovedTimeOff(true);
    }
```

(`ApplyDevBootstrapIfConfigured`'s own `SetConsentValid(true)` stays unchanged — it's an explicit, logged, dev-only escape hatch behind `AllowLocalLifecycleWithoutFullGates`, unrelated to this gap.)

- [ ] **Step 3: Build and run the full Service test suite**

Run: `dotnet build ONEVO.Agent.Service --no-restore` then `dotnet test tests/ONEVO.Agent.Service.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests still green (nothing in the existing suite asserted the literal `"GATES_CLOSED"` string for these two call sites specifically — if something does, update that assertion to match the new call, since the behavior for non-consent gate failures is unchanged).

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/AgentWorker.cs
git commit -m "fix(agent): stop hardcoding consent valid; ClockIn/EndBreak report CONSENT_REQUIRED specifically"
```

---

### Task 8: `OnevoApiClient` — consent fields + accept call

**Files:**
- Modify: `ONEVO.Agent.Service/Api/OnevoApiClient.cs`
- Modify: `ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `OnevoApiClientTests.cs` (mirror the file's existing style for a policy-fetch test and add a body for the new POST route — inspect the file's existing `StubHandler`/`StubHttpClientFactory` helpers already used by `PolicySyncServiceTests.cs` and reuse them):

```csharp
    [Fact]
    public async Task GetEffectivePolicyAsync_MapsConsentFields()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                version = "policy-v3",
                activity_signal_enabled = true,
                app_usage_enabled = true,
                screenshot_enabled = false,
                inactivity_screenshot_enabled = false,
                camera_verification_enabled = false,
                idle_threshold_minutes = 5,
                valid_until = DateTimeOffset.UtcNow.AddHours(1),
                consent_text_version = "1",
                consent_text = "Sample disclosure",
                has_valid_consent = true
            })
        });
        var client = Build(handler);

        var result = await client.GetEffectivePolicyAsync("token", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("1", result.Policy!.ConsentTextVersion);
        Assert.Equal("Sample disclosure", result.Policy.ConsentText);
        Assert.True(result.Policy.HasValidConsent);
    }

    [Fact]
    public async Task AcceptMonitoringConsentAsync_Success_ReturnsTrue()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = Build(handler);

        var success = await client.AcceptMonitoringConsentAsync("token", "1", CancellationToken.None);

        Assert.True(success);
    }

    [Fact]
    public async Task AcceptMonitoringConsentAsync_ServerError_ReturnsFalse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = Build(handler);

        var success = await client.AcceptMonitoringConsentAsync("token", "1", CancellationToken.None);

        Assert.False(success);
    }
```

Place these three tests alongside the existing ones in the file — it already has a `private static OnevoApiClient Build(HttpMessageHandler handler) => new(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);` helper plus `StubHandler`/`StubHttpClientFactory` nested classes (lines 121–140ish) — reuse them as-is, no new helpers needed.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "OnevoApiClientTests" --no-build` (from `C:\HR\tray_app_maui`)
Expected: FAIL to compile — `AcceptMonitoringConsentAsync` doesn't exist, `AgentPolicy` mapping doesn't set the 3 new fields yet.

- [ ] **Step 3: Add the route constant**

Add to `AgentApiRoutes.cs`, next to `TrayPolicy` (near line 14):

```csharp
    public const string TrayConsent           = "/api/v1/monitoring/tray/consent";
```

- [ ] **Step 4: Extend `TrayAgentPolicyPayload` and the mapping in `GetEffectivePolicyAsync`**

In `OnevoApiClient.cs`, extend `TrayAgentPolicyPayload` (near line 320):

```csharp
public sealed record TrayAgentPolicyPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("consent_text_version")] string ConsentTextVersion,
    [property: JsonPropertyName("consent_text")] string ConsentText,
    [property: JsonPropertyName("has_valid_consent")] bool HasValidConsent);
```

And in `GetEffectivePolicyAsync`, extend the `AgentPolicy` construction (near line 103):

```csharp
        var policy = new AgentPolicy
        {
            Version = payload.Version,
            ActivitySignalEnabled = payload.ActivitySignalEnabled,
            AppUsageEnabled = payload.AppUsageEnabled,
            ScreenshotEnabled = payload.ScreenshotEnabled,
            InactivityScreenshotEnabled = payload.InactivityScreenshotEnabled,
            CameraVerificationEnabled = payload.CameraVerificationEnabled,
            IdleThresholdMinutes = payload.IdleThresholdMinutes,
            ValidUntil = payload.ValidUntil,
            ConsentTextVersion = payload.ConsentTextVersion,
            ConsentText = payload.ConsentText,
            HasValidConsent = payload.HasValidConsent
        };
```

- [ ] **Step 5: Add `AcceptMonitoringConsentAsync`**

Add to `OnevoApiClient.cs`, after `GetEffectivePolicyAsync` (near line 116):

```csharp
    /// <summary>Records the employee's acceptance of the current consent disclosure. Auth: Bearer Device JWT.</summary>
    public async Task<bool> AcceptMonitoringConsentAsync(string accessToken, string consentTextVersion, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.TrayConsent)
        {
            Content = JsonContent.Create(new { consentTextVersion })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.TrayConsent);
            return false;
        }
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "OnevoApiClientTests" --no-build`
Expected: PASS.

- [ ] **Step 7: Full Service build + test run**

Run: `dotnet build ONEVO.Agent.Service --no-restore` then `dotnet test tests/ONEVO.Agent.Service.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests green.

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.Service/Api tests/ONEVO.Agent.Service.Tests/Api
git commit -m "feat(agent): map consent policy fields and add AcceptMonitoringConsentAsync"
```

---

### Task 9: `PolicySyncService` sets `LifecycleGate.ConsentValid`

**Files:**
- Modify: `ONEVO.Agent.Service/Sync/PolicySyncService.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Sync/PolicySyncServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PolicySyncServiceTests.cs` — first add a `LifecycleGate` parameter to the `Build` helper (default to a fresh `new LifecycleGate()` when not supplied), then add:

```csharp
    [Fact]
    public async Task RefreshOnceAsync_SetsConsentValid_FromPolicyResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                version = "policy-v4",
                activity_signal_enabled = true,
                app_usage_enabled = true,
                screenshot_enabled = false,
                inactivity_screenshot_enabled = false,
                camera_verification_enabled = false,
                idle_threshold_minutes = 5,
                valid_until = DateTimeOffset.UtcNow.AddHours(1),
                consent_text_version = "1",
                consent_text = "Sample disclosure",
                has_valid_consent = true
            })
        });
        var gate = new LifecycleGate();

        var sut = Build(handler, gate: gate);
        await sut.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.True(gate.Snapshot().ConsentValid);
    }

    [Fact]
    public async Task RefreshOnceAsync_NoValidConsent_LeavesGateClosed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                version = "policy-v5",
                activity_signal_enabled = true,
                app_usage_enabled = true,
                screenshot_enabled = false,
                inactivity_screenshot_enabled = false,
                camera_verification_enabled = false,
                idle_threshold_minutes = 5,
                valid_until = DateTimeOffset.UtcNow.AddHours(1),
                consent_text_version = "1",
                consent_text = "Sample disclosure",
                has_valid_consent = false
            })
        });
        var gate = new LifecycleGate();

        var sut = Build(handler, gate: gate);
        await sut.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.False(gate.Snapshot().ConsentValid);
    }
```

Update the `Build` helper's signature to accept and pass through the gate:

```csharp
    private static PolicySyncService Build(
        HttpMessageHandler handler,
        PolicyCache? cache = null,
        RecordingBroadcaster? broadcaster = null,
        LifecycleGate? gate = null) =>
        new(
            NullLogger<PolicySyncService>.Instance,
            new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance),
            new CredentialStore(),
            cache ?? new PolicyCache(),
            broadcaster ?? new RecordingBroadcaster(),
            gate ?? new LifecycleGate());
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "PolicySyncServiceTests" --no-build` (from `C:\HR\tray_app_maui`)
Expected: FAIL to compile — `PolicySyncService` constructor doesn't take a `LifecycleGate` yet.

- [ ] **Step 3: Implement**

In `PolicySyncService.cs`, add the field, constructor parameter, and the `using`:

```csharp
    using ONEVO.Agent.Service.Lifecycle;
```

```csharp
    private readonly ILogger<PolicySyncService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly PolicyCache _policyCache;
    private readonly IIpcBroadcaster _broadcaster;
    private readonly LifecycleGate _lifecycleGate;

    public PolicySyncService(
        ILogger<PolicySyncService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        PolicyCache policyCache,
        IIpcBroadcaster broadcaster,
        LifecycleGate lifecycleGate)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _policyCache = policyCache;
        _broadcaster = broadcaster;
        _lifecycleGate = lifecycleGate;
    }
```

And in `RefreshOnceAsync`, right after `_policyCache.Set(policy);` (near line 119):

```csharp
        var previousVersion = _policyCache.Current.Version;
        _policyCache.Set(policy);
        _lifecycleGate.SetConsentValid(policy.HasValidConsent);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "PolicySyncServiceTests" --no-build`
Expected: PASS, all tests green (existing + 2 new).

- [ ] **Step 5: Register `LifecycleGate` where `PolicySyncService` is constructed**

`LifecycleGate` is already a singleton used elsewhere (`AgentWorker` takes it directly), so no DI change should be needed — `services.AddHostedService<PolicySyncService>()` in `Program.cs` resolves all constructor parameters from the container automatically, and `LifecycleGate` is already registered as a singleton for `AgentWorker` to share the same instance. Verify this assumption:

Run: `grep -n "AddSingleton<LifecycleGate>" ONEVO.Agent.Service/Program.cs` (from `C:\HR\tray_app_maui`)
Expected: one match. If no match is found, add `services.AddSingleton<LifecycleGate>();` in `Program.cs` next to the other singleton lifecycle registrations, so `AgentWorker` and `PolicySyncService` share one `LifecycleGate` instance — two separate instances would silently break the whole feature (the Service checks the instance `AgentWorker` holds, but only `PolicySyncService`'s instance would ever have `SetConsentValid` called on it).

- [ ] **Step 6: Full Service build + test run**

Run: `dotnet build ONEVO.Agent.Service --no-restore` then `dotnet test tests/ONEVO.Agent.Service.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests green.

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.Service/Sync/PolicySyncService.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/Sync/PolicySyncServiceTests.cs
git commit -m "feat(agent): PolicySyncService applies HasValidConsent to LifecycleGate"
```

---

### Task 10: `AgentWorker` handles `ConsentAcceptSubmit` and re-syncs immediately

**Files:**
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Modify: `ONEVO.Agent.Service/Program.cs`

No new dedicated unit test — same rationale as Task 7 (`AgentWorker` has no unit-test seam). The behavior underneath (`OnevoApiClient.AcceptMonitoringConsentAsync`, `PolicySyncService.RefreshOnceAsync`) is already covered by Tasks 8–9; this task is thin wiring, verified by a full build/test pass and the live check in Task 12.

- [ ] **Step 1: Make `PolicySyncService` resolvable as itself**

In `Program.cs`, next to the existing `services.AddHostedService<PolicySyncService>();` (near line 117), change to:

```csharp
        services.AddSingleton<PolicySyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<PolicySyncService>());
```

This makes `AgentWorker` able to inject the same running `PolicySyncService` instance instead of duplicating its HTTP-fetch-and-broadcast logic.

- [ ] **Step 2: Inject `PolicySyncService` into `AgentWorker`**

Add a `using ONEVO.Agent.Service.Sync;` to the top of `AgentWorker.cs` (near the other `using ONEVO.Agent.Service.*` lines, line 5–13), then add the field, constructor parameter, and assignment (near lines 18–63):

```csharp
    private readonly PolicySyncService _policySync;
```

```csharp
    public AgentWorker(
        ILogger<AgentWorker> logger,
        NamedPipeServer pipeServer,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        ActivityRecordBuffer activityBuffer,
        PresenceSession presenceSession,
        LifecycleGate lifecycleGate,
        IOptions<AgentOptions> options,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        DeviceIdentityStore deviceIdentityStore,
        EnrollmentCoordinator enrollmentCoordinator,
        InactivityEvidenceHandler inactivityEvidence,
        EvidenceSpoolStore evidenceSpool,
        PolicySyncService policySync)
    {
        _logger = logger;
        _pipeServer = pipeServer;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _activityBuffer = activityBuffer;
        _presenceSession = presenceSession;
        _lifecycleGate = lifecycleGate;
        _options = options.Value;
        _apiClient = apiClient;
        _credentials = credentials;
        _deviceIdentityStore = deviceIdentityStore;
        _enrollmentCoordinator = enrollmentCoordinator;
        _inactivityEvidence = inactivityEvidence;
        _evidenceSpool = evidenceSpool;
        _policySync = policySync;
    }
```

- [ ] **Step 3: Add the dispatch case**

In `HandleMessageAsync`'s switch (near line 217, right before the closing `}` of the switch):

```csharp
            case IpcMessageTypes.ConsentAcceptSubmit:
                await HandleConsentAcceptSubmitAsync(envelope, reply);
                break;
```

- [ ] **Step 4: Add the handler**

Add near `HandleBiometricEnrollmentCaptureFinishedAsync` (after line 302):

```csharp
    private async Task HandleConsentAcceptSubmitAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<ConsentAcceptSubmitPayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.ConsentAcceptResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new ConsentAcceptResultPayload(false, "INVALID_PAYLOAD"))
            });
            return;
        }

        var deviceJwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(deviceJwt))
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.ConsentAcceptResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new ConsentAcceptResultPayload(false, "UNAUTHORIZED"))
            });
            return;
        }

        var accepted = await _apiClient.AcceptMonitoringConsentAsync(
            deviceJwt, payload.ConsentTextVersion, CancellationToken.None);

        if (accepted)
        {
            // Re-fetch immediately so ConsentValid flips without waiting for the 45-min cycle —
            // the employee is sitting on the consent screen waiting to proceed to Clock In.
            await _policySync.RefreshOnceAsync(deviceJwt, CancellationToken.None);
        }

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.ConsentAcceptResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new ConsentAcceptResultPayload(accepted, accepted ? null : "SERVICE_UNAVAILABLE"))
        });
    }
```

- [ ] **Step 5: Full Service build + test run**

Run: `dotnet build ONEVO.Agent.Service --no-restore` then `dotnet test tests/ONEVO.Agent.Service.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests green.

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Program.cs
git commit -m "feat(agent): AgentWorker records consent acceptance and re-syncs policy immediately"
```

---

## Part D — TrayApp (`C:\HR\tray_app_maui\ONEVO.Agent.TrayApp`)

### Task 11: `INamedPipeClient.AcceptConsentAsync`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`

No dedicated new test file — `NamedPipeClient`'s send/await pattern is exercised through the ViewModel tests in Task 12, matching how `CompleteBiometricEnrollmentAsync` itself has no direct unit test either (it's a thin IPC transport method).

- [ ] **Step 1: Add to the interface**

Add to `INamedPipeClient.cs`, after `CompleteBiometricEnrollmentAsync` (near line 61):

```csharp
    /// <summary>Submits acceptance of the monitoring consent disclosure and waits for ConsentAcceptResult (or timeout).</summary>
    Task<ConsentAcceptResultPayload?> AcceptConsentAsync(string consentTextVersion, CancellationToken ct);
```

- [ ] **Step 2: Implement in `NamedPipeClient`**

Add to `NamedPipeClient.cs`, after `CompleteBiometricEnrollmentAsync` (near line 363):

```csharp
    public async Task<ConsentAcceptResultPayload?> AcceptConsentAsync(string consentTextVersion, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.ConsentAcceptSubmit,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new ConsentAcceptSubmitPayload(consentTextVersion))
            };
            await WriteEnvelopeAsync(envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            IpcEnvelope reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Consent acceptance timed out waiting for result");
                return null;
            }

            return reply.Payload?.Deserialize<ConsentAcceptResultPayload>();
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }
```

- [ ] **Step 3: Add to the fake**

Add to `FakeNamedPipeClient.cs`, after `CompleteBiometricEnrollmentAsync` (near line 172):

```csharp
    /// <summary>Optional canned result for AcceptConsentAsync. Null = auto-success.</summary>
    public ConsentAcceptResultPayload? NextConsentAcceptResult { get; set; }

    public Task<ConsentAcceptResultPayload?> AcceptConsentAsync(string consentTextVersion, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.ConsentAcceptSubmit,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new ConsentAcceptSubmitPayload(consentTextVersion))
        });

        if (NextConsentAcceptResult is not null)
            return Task.FromResult<ConsentAcceptResultPayload?>(NextConsentAcceptResult);

        return Task.FromResult<ConsentAcceptResultPayload?>(new ConsentAcceptResultPayload(true, null));
    }
```

- [ ] **Step 4: Full TrayApp build**

Run: `dotnet build ONEVO.Agent.TrayApp --no-restore` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors — this compiles even before Task 12 wires the ViewModel, since the fake now satisfies the interface.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs
git commit -m "feat(trayapp): add AcceptConsentAsync to the named pipe client"
```

---

### Task 12: `PrivacyConsentViewModel` records real acceptance; `ClockInViewModel`/`ActiveSessionViewModel` route on `CONSENT_REQUIRED`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`

- [ ] **Step 1: Write the failing `PrivacyConsentViewModel` tests**

Add to `PrivacyConsentViewModelTests.cs`:

```csharp
    [Fact]
    public void ApplyPolicy_SetsConsentTextAndVersion()
    {
        var vm     = new PrivacyConsentViewModel(new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient());
        var policy = new AgentPolicy { Version = "1", ConsentTextVersion = "1", ConsentText = "Sample disclosure" };
        vm.ApplyPolicy(policy);
        Assert.Equal("1", vm.ConsentTextVersion);
        Assert.Equal("Sample disclosure", vm.ConsentText);
    }

    [Fact]
    public async Task AllowAndContinue_OnSuccess_SendsAcceptAndNavigates()
    {
        var pipe = new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient
        {
            NextConsentAcceptResult = new ConsentAcceptResultPayload(true, null)
        };
        var vm = new PrivacyConsentViewModel(pipe);
        vm.ApplyPolicy(new AgentPolicy { Version = "1", ConsentTextVersion = "1", ConsentText = "Sample disclosure" });

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        Assert.Contains(pipe.SentEnvelopes, e => e.Type == IpcMessageTypes.ConsentAcceptSubmit);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task AllowAndContinue_OnFailure_ShowsErrorAndDoesNotNavigate()
    {
        var pipe = new ONEVO.Agent.TrayApp.Tests.Fakes.FakeNamedPipeClient
        {
            NextConsentAcceptResult = new ConsentAcceptResultPayload(false, "SERVICE_UNAVAILABLE")
        };
        var vm = new PrivacyConsentViewModel(pipe);
        vm.ApplyPolicy(new AgentPolicy { Version = "1", ConsentTextVersion = "1", ConsentText = "Sample disclosure" });

        await vm.AllowAndContinueCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "PrivacyConsentViewModelTests" --no-build` (from `C:\HR\tray_app_maui`)
Expected: FAIL to compile — `ConsentTextVersion`/`ConsentText`/`ErrorMessage` properties don't exist on `PrivacyConsentViewModel` yet, and `AllowAndContinueCommand` isn't async yet.

- [ ] **Step 3: Implement `PrivacyConsentViewModel`**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled    = true;
    [ObservableProperty] private bool _locationAccessEnabled = true;
    [ObservableProperty] private bool _cameraAccessEnabled   = false;
    [ObservableProperty] private bool _notificationsEnabled  = true;
    [ObservableProperty] private bool _keyboardMouseEnabled  = true;

    [ObservableProperty] private string _consentTextVersion = string.Empty;
    [ObservableProperty] private string _consentText = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSubmitting;

    public PrivacyConsentViewModel(INamedPipeClient pipe)
    {
        Title = "Allow Required Policies";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled;
        ConsentTextVersion  = policy.ConsentTextVersion;
        ConsentText         = policy.ConsentText;
    }

    [RelayCommand]
    private async Task AllowAndContinue()
    {
        ErrorMessage = null;
        IsSubmitting = true;
        try
        {
            var result = await _pipe.AcceptConsentAsync(ConsentTextVersion, CancellationToken.None);
            if (result is null || !result.Success)
            {
                ErrorMessage = result?.ErrorCode ?? "Could not reach OneXso Agent Service. Please try again.";
                return;
            }

            try { await Shell.Current.GoToAsync("//clockin"); }
            catch { /* unit tests */ }
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "PrivacyConsentViewModelTests" --no-build`
Expected: PASS, all tests green (existing + 3 new).

- [ ] **Step 5: Write the failing `ClockInViewModel` test**

`ClockInViewModelTests.cs` already has `private static ClockInViewModel Make(FakeNamedPipeClient? pipe = null) => new(pipe ?? new FakeNamedPipeClient());` — reuse it. Add:

```csharp
    [Fact]
    public async Task ClockInAsync_OnConsentRequired_NavigatesToPolicy()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, "CONSENT_REQUIRED", "Monitoring gates are not satisfied.", MonitoringState.Stopped, null)
        };
        var vm = Make(pipe);

        await vm.ClockInCommand.ExecuteAsync(null);

        // Navigation itself can't be asserted directly outside Shell in unit tests (existing
        // pattern in this file swallows GoToAsync via try/catch) — assert the routing branch was
        // taken instead of falling into the generic error-message branch:
        Assert.True(vm.NavigatedToPolicy);
        Assert.Null(vm.ErrorMessage);
    }
```

Add a `NavigatedToPolicy` boolean the implementation step below sets, since `Shell.Current.GoToAsync` is swallowed by a try/catch in unit tests exactly like the rest of this ViewModel's navigation calls.

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "ClockInViewModelTests" --no-build`
Expected: FAIL to compile — `NavigatedToPolicy` doesn't exist yet, routing branch not implemented.

- [ ] **Step 7: Implement the routing branch**

In `ClockInViewModel.cs`, `ClockInAsync` (near line 161), change:

```csharp
            if (!result.Success)
            {
                ErrorMessage = result.Message
                    ?? result.ErrorCode
                    ?? "Clock-in failed.";
                return;
            }
```

to:

```csharp
            if (!result.Success)
            {
                if (result.ErrorCode == "CONSENT_REQUIRED")
                {
                    try { await Shell.Current.GoToAsync("//policy"); NavigatedToPolicy = true; }
                    catch { NavigatedToPolicy = true; /* unit tests */ }
                    return;
                }

                ErrorMessage = result.Message
                    ?? result.ErrorCode
                    ?? "Clock-in failed.";
                return;
            }
```

Add the tracking property near the class's other observable/plain properties:

```csharp
    /// <summary>Test-visibility flag — Shell navigation itself can't be asserted outside a real Shell.</summary>
    public bool NavigatedToPolicy { get; private set; }
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "ClockInViewModelTests" --no-build`
Expected: PASS.

- [ ] **Step 9: Apply the same routing to `ActiveSessionViewModel`'s break-resume path**

Read `ActiveSessionViewModel.cs` around its `EndBreak`/resume-from-break lifecycle call (the one already handling `result.ErrorMessage = result.Message ?? result.ErrorCode ?? "Action failed."` per line 385 found earlier) and apply the identical `if (result.ErrorCode == "CONSENT_REQUIRED") { GoToAsync("//policy"); return; }` branch before that fallback, with a matching `NavigatedToPolicy` tracking property and a mirrored test in `ActiveSessionViewModelTests.cs` (find the file's existing break-resume test and copy its setup, only changing `NextLifecycleResult`'s `ErrorCode` to `"CONSENT_REQUIRED"` and asserting the new flag instead of `ErrorMessage`).

- [ ] **Step 10: Full TrayApp build + test run**

Run: `dotnet build ONEVO.Agent.TrayApp --no-restore` then `dotnet test tests/ONEVO.Agent.TrayApp.Tests --no-build` (from `C:\HR\tray_app_maui`)
Expected: 0 warnings/errors, all tests green.

- [ ] **Step 11: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels tests/ONEVO.Agent.TrayApp.Tests/ViewModels
git commit -m "feat(trayapp): PrivacyConsentPage records real acceptance; ClockIn/EndBreak route to it on CONSENT_REQUIRED"
```

---

## Part E — End-to-end verification

### Task 13: Live check with the full stack

**Files:** none (verification only)

- [ ] **Step 1: Run the full stack**

Use the `run` skill (`scripts/run-all.ps1` from `C:\HR\tray_app_maui`) to start Backend + Service + TrayApp together, with PostgreSQL already up.

- [ ] **Step 2: Verify the consent gate blocks a fresh employee**

Enroll a new device via activation code. Confirm the app routes through `PrivacyConsentPage` before `ClockInPage` (existing onboarding route, unchanged), and that the disclosure text shown matches `MonitoringConsentText.Text` from the backend (not the old static toggle labels).

- [ ] **Step 3: Verify acceptance persists and unblocks Clock In**

Tap **Allow and Continue**. Confirm: (a) the app navigates to `ClockInPage`, (b) Clock In succeeds, (c) a row now exists in `employee_monitoring_consents` for this employee at `ConsentTextVersion = "1"` (query the dev DB directly).

- [ ] **Step 4: Verify re-consent on a version bump**

Temporarily change `MonitoringConsentText.CurrentVersion` to `"2"` in the backend, restart the backend, and — without re-enrolling — attempt Clock In again on the already-consented device (or wait for the next policy poll / restart the Service to force an immediate refetch). Confirm the app is routed back to `PrivacyConsentPage` instead of clocking in directly, and that accepting again clocks in successfully. Revert `CurrentVersion` back to `"1"` afterward.

- [ ] **Step 5: Report**

No commit for this task — it's a manual verification pass. If any step fails, return to the relevant task above, fix, and re-run its tests before re-attempting this verification.
