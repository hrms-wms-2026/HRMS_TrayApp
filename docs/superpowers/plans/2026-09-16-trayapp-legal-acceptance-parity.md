# TrayApp Terms &amp; Conditions Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The TrayApp checks the same backend-tracked legal-acceptance state
the web login already checks, and only prompts the employee when a
published document version they haven't accepted yet is pending — never
unconditionally, and never permanently skipped once a new version is
published.

**Architecture:** Reuse the existing `LegalDocumentVersion` /
`LegalAcceptanceRecord` / `LegalAcceptanceChecker` /
`AuthPendingLegalController` system as-is. Call `LegalAcceptanceChecker.CheckAsync`
from both places the tray receives credentials — `TrayEnrollmentService.IssueAsync`
and `RefreshTrayTokenCommandHandler` — and thread the result through
`TrayAuthResponseDto`/`TrayAuthPayload` to a new tray-side screen that posts
to the same `complete-login` endpoint the web app already uses.

**Tech Stack:** .NET 8 / EF Core (HRMS-Backend-v1, xUnit), .NET MAUI
(HRMS_TrayApp, xUnit).

**Spec:** [docs/superpowers/specs/2026-09-16-trayapp-legal-acceptance-parity-design.md](../specs/2026-09-16-trayapp-legal-acceptance-parity-design.md)

## Global Constraints

- No new backend entity, endpoint, or migration — reuse
  `ILegalAcceptanceChecker`, `PendingLegalDocumentDto`, and
  `POST /api/v1/legal/acceptances/complete-login` exactly as they exist
  today.
- `PrivacyTransparencyPage`/`PrivacyTransparencyViewModel` are out of scope
  — do not touch them.
- Both the enrollment path and the refresh path must carry the new fields —
  skipping the refresh path defeats the "already-enrolled users must also
  see a newly published document" requirement.

---

### Task 1: Backend — `TrayAuthResponseDto` gains legal-acceptance fields

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/DTOs/TrayAuthResponseDtoTests.cs` (create if no existing DTO test file — a simple serialization-shape test is enough)

**Interfaces:**
- Produces: `TrayAuthResponseDto.RequiresLegalAcceptance` (bool), `TrayAuthResponseDto.PendingLegalDocuments` (`IReadOnlyList<PendingLegalDocumentDto>`) — consumed by Tasks 2 and 4.

- [ ] **Step 1: Write the failing serialization test**

```csharp
[Fact]
public void TrayAuthResponseDto_SerializesLegalAcceptanceFieldsWithSnakeCaseNames()
{
    var dto = new TrayAuthResponseDto(
        AccessToken: "token", ExpiresInSeconds: 3600, RefreshToken: "refresh", RefreshExpiresInSeconds: 7776000,
        RequiresLegalAcceptance: true,
        PendingLegalDocuments: new[] { new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash") });

    var json = JsonSerializer.Serialize(dto);

    Assert.Contains("\"legal_acceptance_required\":true", json);
    Assert.Contains("\"pending_legal_documents\":[", json);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter TrayAuthResponseDtoTests`
Expected: FAIL (constructor doesn't accept these named parameters yet).

- [ ] **Step 2: Add the fields**

```csharp
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Auth.Legal.DTOs; // wherever PendingLegalDocumentDto actually lives - check LegalAcceptanceChecker.cs's using block

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public record TrayAuthResponseDto(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName = null,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail = null,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber = null,
    [property: JsonPropertyName("employee_profile_status")] string EmployeeProfileStatus = "resolved",
    [property: JsonPropertyName("tenant_slug")] string? TenantSlug = null,
    [property: JsonPropertyName("legal_acceptance_required")] bool RequiresLegalAcceptance = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocumentDto>? PendingLegalDocuments = null);
```

(Confirm `PendingLegalDocumentDto`'s actual namespace by checking the
`using` block at the top of `LegalAcceptanceChecker.cs` before writing the
`using` line above — it's referenced there without a fully-qualified name.)

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS. Also run
`dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter TrayEnrollmentServiceTests`
and `--filter PollDeviceAuthorizationCommandHandlerTests` /
`ExchangeActivationCodeCommandHandlerTests` to confirm the two new optional
parameters (both default to safe values) don't break any existing call site
that constructs a `TrayAuthResponseDto` positionally — if any test fails
here, it's constructing the record positionally rather than with named
arguments; fix that call site to use named arguments instead of reordering
this DTO.

- [ ] **Step 4: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/DTOs/Responses/TrayAuthResponseDto.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/DTOs/TrayAuthResponseDtoTests.cs
git commit -m "feat(backend): add legal acceptance fields to TrayAuthResponseDto"
```

---

### Task 2: Backend — wire the check into `TrayEnrollmentService.IssueAsync`

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs`
- Test: `HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Services/TrayEnrollmentServiceTests.cs` (same file Plan A's Task 3 extends — if executing this plan independently of Plan A, create it)

**Interfaces:**
- Consumes: `ILegalAcceptanceChecker.CheckAsync(Guid tenantId, Guid userId, CancellationToken ct)` (existing, `LegalAcceptanceChecker.cs:23`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task IssueAsync_WhenLegalCheckerReportsPending_SetsRequiresLegalAcceptanceAndDocuments()
{
    var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
    LegalChecker.CheckAsyncResult = new LegalAcceptanceCheckResult(
        LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc });

    var result = await Service.IssueAsync(ValidRequest, CancellationToken.None);

    Assert.True(result.RequiresLegalAcceptance);
    Assert.Single(result.PendingLegalDocuments!);
}

[Fact]
public async Task IssueAsync_WhenLegalCheckerReportsComplete_LeavesRequiresLegalAcceptanceFalse()
{
    LegalChecker.CheckAsyncResult = new LegalAcceptanceCheckResult(
        LegalAcceptanceStatus.Complete, IsComplete: true, PendingDocuments: Array.Empty<PendingLegalDocumentDto>());

    var result = await Service.IssueAsync(ValidRequest, CancellationToken.None);

    Assert.False(result.RequiresLegalAcceptance);
    Assert.Empty(result.PendingLegalDocuments ?? Array.Empty<PendingLegalDocumentDto>());
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter TrayEnrollmentServiceTests`
Expected: FAIL (constructor doesn't take `ILegalAcceptanceChecker` yet).

- [ ] **Step 2: Inject the checker and populate the response**

Add `ILegalAcceptanceChecker legalChecker` to the constructor's parameter
list (field `_legalChecker`), then in `IssueAsync`, right before building
the `return new TrayAuthResponseDto(...)` statement:

```csharp
var legalCheck = await _legalChecker.CheckAsync(request.TenantId, request.UserId, ct);

return new TrayAuthResponseDto(
    accessToken,
    AccessTokenExpiresInSeconds,
    rawRefreshToken,
    RefreshTokenExpiresInSeconds,
    employeeName,
    employeeEmail,
    employeeNumber,
    profileStatus,
    tenantSlug,
    RequiresLegalAcceptance: !legalCheck.IsComplete,
    PendingLegalDocuments: legalCheck.PendingDocuments);
```

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Services/TrayEnrollmentService.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Services/TrayEnrollmentServiceTests.cs
git commit -m "feat(backend): surface legal acceptance status on tray enrollment"
```

---

### Task 3: Backend — wire the same check into `RefreshTrayTokenCommandHandler`

**Files:**
- Modify: `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs`
- Test: extend `RefreshTrayTokenCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILegalAcceptanceChecker.CheckAsync` (same as Task 2).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_WhenLegalCheckerReportsPending_SetsRequiresLegalAcceptanceOnRefreshResponse()
{
    var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
    LegalChecker.CheckAsyncResult = new LegalAcceptanceCheckResult(
        LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc });

    var result = await Handler.Handle(ValidRefreshCommand, CancellationToken.None);

    Assert.True(result.Value!.RequiresLegalAcceptance);
}
```

Run: `dotnet test HRMS-Backend-v1/tests/ONEVO.Application.Tests --filter RefreshTrayTokenCommandHandlerTests`
Expected: FAIL.

- [ ] **Step 2: Inject the checker and populate the response**

Add `ILegalAcceptanceChecker legalChecker` to the constructor (field
`_legalChecker`), then before the final `return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(...))`:

```csharp
var legalCheck = await _legalChecker.CheckAsync(existingToken.TenantId, existingToken.UserId, cancellationToken);

return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
    accessToken,
    AccessTokenExpiresInSeconds,
    newRawToken,
    RefreshTokenExpiresInSeconds,
    employeeName,
    employeeEmail,
    employeeNumber,
    profileStatus,
    RequiresLegalAcceptance: !legalCheck.IsComplete,
    PendingLegalDocuments: legalCheck.PendingDocuments));
```

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken/RefreshTrayTokenCommandHandler.cs HRMS-Backend-v1/tests/ONEVO.Application.Tests/Features/Monitoring/TrayActivation/Commands/RefreshTrayToken
git commit -m "feat(backend): surface legal acceptance status on tray token refresh"
```

---

### Task 4: TrayApp — extend `TrayAuthPayload` and deserialize the new fields

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs:744-757`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs` (extend)

**Interfaces:**
- Produces: `TrayAuthPayload.RequiresLegalAcceptance` (bool), `TrayAuthPayload.PendingLegalDocuments` (list of a new `PendingLegalDocument` record) — consumed by Task 5.

- [ ] **Step 1: Write the failing deserialization test**

```csharp
[Fact]
public void TrayAuthPayload_DeserializesLegalAcceptanceFields()
{
    var json = """
    {
      "access_token": "t", "expires_in_seconds": 3600,
      "refresh_token": "r", "refresh_expires_in_seconds": 7776000,
      "legal_acceptance_required": true,
      "pending_legal_documents": [
        { "document_type": "privacy_policy", "version": "2.0", "title": "Privacy Policy",
          "content_endpoint": "/api/v1/legal/documents/privacy_policy/2.0" }
      ]
    }
    """;

    var payload = JsonSerializer.Deserialize<TrayAuthPayload>(json, OnevoApiClient.JsonOptions);

    Assert.True(payload!.RequiresLegalAcceptance);
    Assert.Single(payload.PendingLegalDocuments!);
    Assert.Equal("privacy_policy", payload.PendingLegalDocuments![0].DocumentType);
}
```

(Use whichever `JsonSerializerOptions` instance `OnevoApiClient` already
uses to deserialize its responses — check the existing deserialization call
site rather than assuming a name.)

Run: `dotnet test HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests --filter OnevoApiClientTests`
Expected: FAIL.

- [ ] **Step 2: Extend the payload record**

```csharp
public sealed record PendingLegalDocument(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_url")] string? ContentUrl,
    [property: JsonPropertyName("content_endpoint")] string ContentEndpoint,
    [property: JsonPropertyName("content_hash")] string? ContentHash);

public sealed record TrayAuthPayload(
    // ... existing properties unchanged ...
    [property: JsonPropertyName("legal_acceptance_required")] bool RequiresLegalAcceptance = false,
    [property: JsonPropertyName("pending_legal_documents")] IReadOnlyList<PendingLegalDocument>? PendingLegalDocuments = null);
```

- [ ] **Step 3: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add HRMS_TrayApp/ONEVO.Agent.Service/Api/OnevoApiClient.cs HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/Api/OnevoApiClientTests.cs
git commit -m "feat(tray): deserialize legal acceptance fields from tray auth responses"
```

---

### Task 5: TrayApp — route to a pending-acceptance state after enroll/refresh

**Files:**
- Modify: `HRMS_TrayApp/ONEVO.Agent.Service/AgentWorker.cs` (both the successful-enrollment path and the silent-refresh path — find where `EmployeeProfileStatus == "company_context_required"` is currently branched on, at approximately lines 90-204 per the earlier investigation, and add a sibling branch)
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/AgentWorkerDevicePairingTests.cs` (extend existing — this file already covers device pairing state transitions)

**Interfaces:**
- Consumes: `TrayAuthPayload.RequiresLegalAcceptance`/`PendingLegalDocuments` (Task 4).
- Produces: a new state/event the UI layer (Task 6) observes — match whichever mechanism `AgentWorker` already uses to notify the UI of `company_context_required` (an event, a published state enum, or a preference flag — read that exact mechanism before choosing one here, so this doesn't introduce a second, inconsistent notification pattern).

- [ ] **Step 1: Read the existing `company_context_required` handling first**

Find every place `AgentWorker.cs` checks
`EmployeeProfileStatus == "company_context_required"` and trace how that
state reaches the UI (event, IPC message, preference key). This task's new
`RequiresLegalAcceptance` branch must use the exact same mechanism, not a
new one.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task OnEnrollmentSucceeds_WhenRequiresLegalAcceptance_SurfacesPendingLegalAcceptanceState()
{
    Api.EnrollResult = ValidPayloadWith(requiresLegalAcceptance: true, pendingDocs: new[] { SamplePendingDoc });

    await Worker.HandleEnrollmentAsync(ValidActivationCode, CancellationToken.None);

    Assert.True(Worker.LastSurfacedState.RequiresLegalAcceptance);
}

[Fact]
public async Task OnEnrollmentSucceeds_WhenLegalAcceptanceNotRequired_SkipsPendingLegalAcceptanceState()
{
    Api.EnrollResult = ValidPayloadWith(requiresLegalAcceptance: false, pendingDocs: null);

    await Worker.HandleEnrollmentAsync(ValidActivationCode, CancellationToken.None);

    Assert.False(Worker.LastSurfacedState.RequiresLegalAcceptance);
}
```

(Name the test methods/fixtures to match `AgentWorkerDevicePairingTests.cs`'s
actual existing conventions — these are illustrative of the two cases to
cover, not literal required names.)

Run the test file. Expected: FAIL.

- [ ] **Step 3: Add the branch using the mechanism found in Step 1**

Implementation shape depends on Step 1's finding — the requirement is: when
`payload.RequiresLegalAcceptance` is true (from either the enrollment
response or a silent refresh response), surface `payload.PendingLegalDocuments`
to the UI layer via the same channel `company_context_required` already
uses, instead of proceeding straight to the normal connected/resumed state.
When false, proceed exactly as today (no behavior change).

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HRMS_TrayApp/ONEVO.Agent.Service/AgentWorker.cs HRMS_TrayApp/tests/ONEVO.Agent.Service.Tests/AgentWorkerDevicePairingTests.cs
git commit -m "feat(tray): surface pending legal acceptance after enrollment and refresh"
```

---

### Task 6: TrayApp — `LegalConsentPage`/`LegalConsentViewModel`

**Files:**
- Create: `HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/LegalConsentPage.xaml` (+ `.xaml.cs`)
- Create: `HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/LegalConsentViewModel.cs`
- Test: `HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/LegalConsentViewModelTests.cs`

**Interfaces:**
- Consumes: the pending-legal-acceptance state produced by Task 5;
  `POST /api/v1/legal/acceptances/complete-login` (existing backend
  endpoint, `AuthPendingLegalController.cs:24`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task AcceptCommand_PostsAllPendingDocumentIdentifiersAndTransitionsToConnected()
{
    ViewModel.PendingDocuments = new[] { SamplePendingDoc1, SamplePendingDoc2 };

    await ViewModel.AcceptCommand.ExecuteAsync(null);

    Assert.Single(Api.CompleteLoginCalls);
    Assert.Equal(2, Api.CompleteLoginCalls[0].DocumentIdentifiers.Count);
    Assert.True(ViewModel.Completed);
}

[Fact]
public async Task AcceptCommand_WhenBackendCallFails_ShowsRetryableErrorAndDoesNotComplete()
{
    Api.CompleteLoginThrows = new HttpRequestException("network down");
    ViewModel.PendingDocuments = new[] { SamplePendingDoc1 };

    await ViewModel.AcceptCommand.ExecuteAsync(null);

    Assert.False(ViewModel.Completed);
    Assert.NotNull(ViewModel.ErrorMessage);
}
```

Run the test file. Expected: FAIL (types don't exist).

- [ ] **Step 2: Write the ViewModel**

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class LegalConsentViewModel : BaseViewModel
{
    private readonly IOnevoApiClient _api;

    [ObservableProperty] private IReadOnlyList<PendingLegalDocument> _pendingDocuments = Array.Empty<PendingLegalDocument>();
    [ObservableProperty] private bool _completed;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSubmitting;

    public LegalConsentViewModel(IOnevoApiClient api) => _api = api;

    [RelayCommand]
    private async Task AcceptAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        IsSubmitting = true;
        try
        {
            await _api.CompleteLegalAcceptanceAsync(
                PendingDocuments.Select(d => new LegalDocumentIdentifier(d.DocumentType, d.Version)).ToArray(), ct);
            Completed = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't submit your acceptance: {ex.Message}. Try again.";
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
```

(Add a `CompleteLegalAcceptanceAsync` method to `OnevoApiClient`/`IOnevoApiClient`
if one doesn't already exist, posting to
`/api/v1/legal/acceptances/complete-login` with the pending document
type/version pairs — follow whatever request/response shape
`AuthPendingLegalController.cs:24-60` actually expects, read that
controller's request contract before writing this call.)

- [ ] **Step 3: Write the page (visual style matching `PrivacyTransparencyPage.xaml`)**

List each `PendingDocuments` entry as a card with title + version, a link
that opens `ContentUrl` (falling back to fetching `ContentEndpoint` if
`ContentUrl` is null), and a single "Accept and continue" button bound to
`AcceptCommand`, disabled while `IsSubmitting`, showing `ErrorMessage` when
set. Navigate to the normal connected-state page when `Completed` becomes
true.

- [ ] **Step 4: Run tests, confirm pass**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/LegalConsentPage.xaml HRMS_TrayApp/ONEVO.Agent.TrayApp/Views/LegalConsentPage.xaml.cs HRMS_TrayApp/ONEVO.Agent.TrayApp/ViewModels/LegalConsentViewModel.cs HRMS_TrayApp/tests/ONEVO.Agent.TrayApp.Tests/ViewModels/LegalConsentViewModelTests.cs
git commit -m "feat(tray): add legal consent screen for pending document acceptance"
```
