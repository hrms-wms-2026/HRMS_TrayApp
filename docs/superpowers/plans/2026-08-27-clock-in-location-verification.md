# Clock-In Location Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add onboarding work-location confirmation and compare a fresh GPS fix at every Clock In, warning the employee and queuing a durable server event when the location differs.

**Architecture:** The MAUI tray owns permission prompts, UI, and local geofence evaluation. A typed activation-scoped reference is persisted separately from the attempt-scoped current fix; an in-memory context carries the current result through optional face verification. The Windows service receives the typed result in the existing lifecycle IPC message and queues an idempotent collection record through the existing SQLite/offline-sync pipeline.

**Tech Stack:** .NET 10, .NET MAUI Windows, CommunityToolkit.Mvvm, System.Text.Json, Windows App SDK notifications, xUnit, SQLite collection buffer.

**Spec:** `docs/superpowers/specs/2026-08-27-clock-in-location-verification-design.md`

## Global Constraints

- Read location only during setup confirmation and a user-initiated Clock In attempt.
- Do not add background or continuous location tracking.
- Do not use IP-based location or reverse-geocode a street address.
- Use `300 m` for Office and `250 m` for Work From Home/Other Approved Location references.
- Treat current GPS accuracy worse than `100 m` as `Inaccurate`, never as a confirmed mismatch.
- Mismatch is warning-only: require explicit `Clock In Anyway`, but do not permanently block attendance.
- Preserve the current face-verification route and lifecycle ordering.
- Clear activation-scoped location data on successful Sign Out; preserve device identity and completed session history.
- Follow TDD for every behaviour change and do not alter unrelated dirty-worktree files.

---

## File Map

| File | Responsibility |
|---|---|
| `ONEVO.Agent.Shared/Models/WorkLocationModels.cs` | Shared GPS fix, saved reference, verdict, lifecycle result, and collection payload contracts. |
| `ONEVO.Agent.Shared/Models/CollectionRecord.cs` | Adds record type/schema constants for location verification. |
| `ONEVO.Agent.Shared/IPC/IpcMessages.cs` | Adds optional location verification to `LifecycleCommandPayload`. |
| `ONEVO.Agent.TrayApp/Services/ILocationService.cs` | Testable location-capture contract with explicit failure reasons. |
| `ONEVO.Agent.TrayApp/Services/GeolocationService.cs` | MAUI/Windows permission and GPS implementation. |
| `ONEVO.Agent.TrayApp/Services/IGeofenceEvaluator.cs` | Pure Haversine comparison contract. |
| `ONEVO.Agent.TrayApp/Services/GeofenceEvaluator.cs` | Accuracy-aware match/mismatch logic. |
| `ONEVO.Agent.TrayApp/Services/IWorkLocationStore.cs` | Typed reference persistence contract. |
| `ONEVO.Agent.TrayApp/Services/PreferencesWorkLocationStore.cs` | JSON persistence through `IPreferencesStore`. |
| `ONEVO.Agent.TrayApp/Services/ClockInLocationContext.cs` | In-memory hand-off across the optional face screen. |
| `ONEVO.Agent.TrayApp/Services/SessionPreferenceKeys.cs` | Adds the reference key and testable clearing. |
| `ONEVO.Agent.TrayApp/Services/IPreferencesStore.cs` | Adds `Remove`. |
| `ONEVO.Agent.TrayApp/Services/PreferencesStore.cs` | Implements `Remove`. |
| `ONEVO.Agent.TrayApp/Services/IUserNotificationService.cs` | Test seam for warning notifications. |
| `ONEVO.Agent.TrayApp/Services/NotificationService.cs` | Implements local Windows warnings. |
| `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs` | Setup detection, three choices, confirmation, and persistence. |
| `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml(.cs)` | Dedicated location confirmation screen. |
| `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs` | Reloads completion status and gates Continue. |
| `ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml` | Adds Work Location card and completion copy. |
| `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs` | Fresh capture, warning state, retry/cancel/anyway flow, and normal Clock In handoff. |
| `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml` | Renders the actionable warning panel. |
| `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs` | Carries the location verification into camera-gated Clock In. |
| `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs` | Accepts optional location verification on lifecycle calls. |
| `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs` | Serializes the added IPC field. |
| `ONEVO.Agent.TrayApp/Views/AppShell.xaml` | Restores the `location` route. |
| `ONEVO.Agent.TrayApp/MauiProgram.cs` | Registers location, geofence, store, context, page, and ViewModel services. |
| `ONEVO.Agent.Service/Location/ClockInLocationRecordFactory.cs` | Builds deterministic durable collection records. |
| `ONEVO.Agent.Service/AgentWorker.cs` | Queues verification after successful Clock In. |
| `tests/...` | Unit tests and fakes for every boundary above. |

---

### Task 1: Shared location contracts and pure geofence evaluation

**Files:**
- Create: `ONEVO.Agent.Shared/Models/WorkLocationModels.cs`
- Create: `ONEVO.Agent.TrayApp/Services/IGeofenceEvaluator.cs`
- Create: `ONEVO.Agent.TrayApp/Services/GeofenceEvaluator.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/GeofenceEvaluatorTests.cs`

**Interfaces:**
- Produces: `GeoLocationFix`, `WorkLocationKind`, `WorkLocationReference`, `LocationVerificationVerdict`, `ClockInLocationVerification`, `IGeofenceEvaluator.Evaluate`.
- Consumes: no platform APIs; this task must remain a pure deterministic unit.

- [ ] **Step 1: Write failing distance and verdict tests**

```csharp
public sealed class GeofenceEvaluatorTests
{
    private static readonly WorkLocationReference Reference = new(
        WorkLocationKind.WorkFromHome, "WFH", "Work From Home",
        6.9271, 79.8612, 20, 250, DateTimeOffset.Parse("2026-08-27T00:00:00Z"));

    [Fact]
    public void Evaluate_InsideRadius_ReturnsMatch()
    {
        var current = new GeoLocationFix(6.9272, 79.8612, 15, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Match, result.Verdict);
        Assert.True(result.DistanceMeters < result.EffectiveRadiusMeters);
    }

    [Fact]
    public void Evaluate_OutsideRadius_ReturnsMismatch()
    {
        var current = new GeoLocationFix(6.9371, 79.8612, 15, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Mismatch, result.Verdict);
    }

    [Fact]
    public void Evaluate_PoorAccuracy_ReturnsInaccurate_NotMismatch()
    {
        var current = new GeoLocationFix(6.9371, 79.8612, 180, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator(maxAcceptedAccuracyMeters: 100)
            .Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Inaccurate, result.Verdict);
        Assert.Equal("LOW_ACCURACY", result.Reason);
    }

    [Fact]
    public void Evaluate_AccuracySumCanExpandEffectiveRadius()
    {
        var looseReference = Reference with { RadiusMeters = 100, AccuracyMeters = 70 };
        var current = new GeoLocationFix(6.9282, 79.8612, 60, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, looseReference);
        Assert.Equal(130, result.EffectiveRadiusMeters);
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~GeofenceEvaluatorTests
```

Expected: compile failure because the contracts and evaluator do not exist.

- [ ] **Step 3: Add the shared records and evaluator implementation**

```csharp
public enum WorkLocationKind { Office, WorkFromHome, OtherApprovedLocation }
public enum LocationVerificationVerdict { Match, Mismatch, Unavailable, Inaccurate }

public sealed record GeoLocationFix(
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    DateTimeOffset CapturedAt);

public sealed record WorkLocationReference(
    WorkLocationKind Kind,
    string Code,
    string DisplayName,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double RadiusMeters,
    DateTimeOffset ConfirmedAt);

public sealed record ClockInLocationVerification(
    Guid AttemptId,
    GeoLocationFix? CurrentFix,
    WorkLocationReference Reference,
    LocationVerificationVerdict Verdict,
    double? DistanceMeters,
    double EffectiveRadiusMeters,
    string? Reason);
```

```csharp
public interface IGeofenceEvaluator
{
    ClockInLocationVerification Evaluate(
        Guid attemptId,
        GeoLocationFix current,
        WorkLocationReference reference);
}
```

`GeofenceEvaluator` must:

1. reject accuracy greater than `100` with `Inaccurate`;
2. calculate Haversine distance in metres;
3. set effective radius to `Math.Max(reference.RadiusMeters, referenceAccuracy + currentAccuracy)`;
4. return `Match` when `distance <= effectiveRadius`, otherwise `Mismatch`.

- [ ] **Step 4: Run the focused tests and verify green**

Use the Step 2 command. Expected: all `GeofenceEvaluatorTests` pass.

- [ ] **Step 5: Checkpoint the diff**

Review only the four files in this task. Commit as `feat: add location geofence contracts` only when commit authorization is present.

---

### Task 2: Explicit location capture and typed reference persistence

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/ILocationService.cs`
- Create: `ONEVO.Agent.TrayApp/Services/GeolocationService.cs`
- Create: `ONEVO.Agent.TrayApp/Services/IWorkLocationStore.cs`
- Create: `ONEVO.Agent.TrayApp/Services/PreferencesWorkLocationStore.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/IPreferencesStore.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/PreferencesStore.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/SessionPreferenceKeys.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakePreferencesStore.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/PreferencesWorkLocationStoreTests.cs`

**Interfaces:**
- Consumes: Task 1's `GeoLocationFix` and `WorkLocationReference`.
- Produces: `ILocationService.GetCurrentAsync`, `LocationCaptureResult`, and `IWorkLocationStore`.

- [ ] **Step 1: Write failing persistence/clear tests**

```csharp
[Fact]
public void SaveThenLoad_RoundTripsTypedReference()
{
    var prefs = new FakePreferencesStore();
    var store = new PreferencesWorkLocationStore(prefs);
    var reference = new WorkLocationReference(
        WorkLocationKind.Office, "OFFICE", "Office",
        6.9271, 79.8612, 12, 300, DateTimeOffset.Parse("2026-08-27T01:00:00Z"));

    store.Save(reference);

    Assert.Equal(reference, store.Load());
}

[Fact]
public void SessionClear_RemovesSavedReference()
{
    var prefs = new FakePreferencesStore();
    var store = new PreferencesWorkLocationStore(prefs);
    store.Save(new WorkLocationReference(
        WorkLocationKind.Office, "OFFICE", "Office",
        6.9271, 79.8612, 12, 300, DateTimeOffset.UtcNow));

    SessionPreferenceKeys.ClearAll(prefs);

    Assert.Null(store.Load());
}
```

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~PreferencesWorkLocationStoreTests
```

Expected: compile failure because `Remove`, the typed store, and the reference key are missing.

- [ ] **Step 3: Add storage contracts and implementations**

Extend preferences with:

```csharp
public interface IPreferencesStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
    void Remove(string key);
}
```

Add `SessionPreferenceKeys.WorkLocationReference = "onevo.work_location_reference"`, include it in `All`, and replace direct MAUI removal with:

```csharp
public static void ClearAll(IPreferencesStore preferences)
{
    foreach (var key in All)
        preferences.Remove(key);
}
```

Update both existing call sites to pass their injected `_preferences`. `PreferencesWorkLocationStore` serializes/deserializes with `System.Text.Json` and returns `null` for missing or malformed data.

- [ ] **Step 4: Define explicit capture failures and implement Windows capture**

```csharp
public enum LocationCaptureFailure
{
    PermissionDenied,
    ServicesDisabled,
    NotSupported,
    TimedOut,
    Unavailable
}

public sealed record LocationCaptureResult(
    GeoLocationFix? Fix,
    LocationCaptureFailure? Failure)
{
    public bool IsSuccess => Fix is not null;
    public static LocationCaptureResult Success(GeoLocationFix fix) => new(fix, null);
    public static LocationCaptureResult Failed(LocationCaptureFailure failure) => new(null, failure);
}

public interface ILocationService
{
    Task<LocationCaptureResult> GetCurrentAsync(CancellationToken ct = default);
}
```

`GeolocationService` requests `Permissions.LocationWhenInUse`, uses high accuracy with a 12-second timeout, preserves `Location.Accuracy`, and maps `PermissionException`, `FeatureNotEnabledException`, `FeatureNotSupportedException`, and timeout separately.

- [ ] **Step 5: Run persistence tests and the existing suite**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~PreferencesWorkLocationStoreTests
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
```

Expected: focused tests and all existing tray tests pass after fake/call-site updates.

- [ ] **Step 6: Checkpoint the diff**

Review storage migration carefully; confirm no installation-scoped identity or session-history files are cleared. Commit as `feat: persist activation work location` only when authorized.

---

### Task 3: Restore the location-confirmation screen and setup-page gating

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`
- Create: `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml`
- Create: `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/AppShell.xaml`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeLocationService.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `ILocationService`, `IWorkLocationStore`, and Task 1 models.
- Produces: three exact selection options, setup completion state, and navigation back to setup.

- [ ] **Step 1: Write failing WorkLocation ViewModel tests**

```csharp
[Fact]
public async Task DetectAndConfirm_SavesLiveFixAsReference()
{
    var fix = new GeoLocationFix(6.9271, 79.8612, 18, DateTimeOffset.UtcNow);
    var location = new FakeLocationService(LocationCaptureResult.Success(fix));
    var store = new FakeWorkLocationStore();
    var vm = new WorkLocationViewModel(location, store);

    await vm.DetectLocationCommand.ExecuteAsync(null);
    vm.SelectLocationCommand.Execute(vm.Options.Single(x => x.Code == "WFH"));
    await vm.ConfirmLocationCommand.ExecuteAsync(null);

    Assert.Equal(WorkLocationKind.WorkFromHome, store.Value!.Kind);
    Assert.Equal(250, store.Value.RadiusMeters);
    Assert.Equal(fix.Latitude, store.Value.Latitude);
}

[Fact]
public async Task DetectFailure_DoesNotEnableConfirmation()
{
    var vm = new WorkLocationViewModel(
        new FakeLocationService(LocationCaptureResult.Failed(LocationCaptureFailure.PermissionDenied)),
        new FakeWorkLocationStore());
    await vm.DetectLocationCommand.ExecuteAsync(null);
    vm.SelectLocationCommand.Execute(vm.Options[0]);
    Assert.False(vm.ConfirmLocationCommand.CanExecute(null));
    Assert.Contains("Windows Location Services", vm.ErrorMessage);
}

[Fact]
public void Options_AreExactlyTheApprovedThree()
{
    var vm = new WorkLocationViewModel(new FakeLocationService(), new FakeWorkLocationStore());
    Assert.Equal(["Office", "Work From Home", "Other Approved Location"],
        vm.Options.Select(x => x.DisplayName));
}
```

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~WorkLocationViewModelTests
```

Expected: compile failure because the restored ViewModel and fakes do not exist.

- [ ] **Step 3: Implement WorkLocationViewModel**

Use these exact options/radii:

```csharp
public IReadOnlyList<WorkLocationOption> Options { get; } =
[
    new(WorkLocationKind.Office, "OFFICE", "Office", "At your registered office", 300),
    new(WorkLocationKind.WorkFromHome, "WFH", "Work From Home", "Remote location", 250),
    new(WorkLocationKind.OtherApprovedLocation, "OTHER", "Other Approved Location",
        "Client site or approved external workplace", 250)
];
```

`ConfirmLocationCommand` is enabled only when a selection and successful live fix exist. It saves a `WorkLocationReference` and navigates to `//prepare`.

- [ ] **Step 4: Build the dedicated page matching the approved visual direction**

The XAML must include:

- title `Confirm Today's Work Location`;
- detected-location status card with accuracy and verified/failure state;
- the three radio-style options in the exact order above;
- `Confirm Location` primary action;
- `Refresh detection` secondary action;
- existing OneXso header, gradient background, version, and connection footer;
- no vertical clipping at the app's supported minimum height.

The code-behind calls `DetectLocationCommand` on first appearance only; `Refresh detection` starts subsequent requests explicitly.

- [ ] **Step 5: Add setup completion tests before changing setup code**

```csharp
[Fact]
public void CanContinue_RequiresLocationAndFace()
{
    var store = new FakeWorkLocationStore { Value = AReference() };
    var prefs = new FakePreferencesStore();
    var vm = new PrepareWorkspaceViewModel(prefs, store);
    vm.ActivationVerified = vm.UserDetailsFetched = vm.WorkspacePrepared = true;

    Assert.False(vm.CanContinue);
    prefs.Set(SessionPreferenceKeys.FaceVerified, bool.TrueString);
    vm.RefreshCompletionState();
    Assert.True(vm.CanContinue);
}
```

Store the face flag consistently through `IPreferencesStore` as a string; do not mix direct `Preferences.Set(bool)` with the injected seam.

- [ ] **Step 6: Update setup and photo navigation**

`PrepareWorkspaceViewModel` adds:

- `IsLocationVerified`, `LocationStatusText`;
- `IsFaceVerified`, `FaceStatusText`;
- `NavigateToLocationCommand` -> `//location`;
- `NavigateToPhotoCommand` -> `//photo?context=setup`;
- `ContinueSetupCommand` -> `//review`;
- `CanContinue` requiring preparation + location + face.

`PhotoCaptureWindowViewModel` handles `context=setup` by saving the face flag and returning to `//prepare`; the existing `context=clockin` path remains unchanged in this task.

The setup XAML renders Work Location above Profile Picture and displays completion text on both cards.

- [ ] **Step 7: Restore route and dependency injection**

Add `<ShellContent Route="location" ... />` and register:

```csharp
builder.Services.AddSingleton<ILocationService, GeolocationService>();
builder.Services.AddSingleton<IGeofenceEvaluator, GeofenceEvaluator>();
builder.Services.AddSingleton<IWorkLocationStore, PreferencesWorkLocationStore>();
builder.Services.AddTransient<WorkLocationViewModel>();
builder.Services.AddTransient<WorkLocationPage>();
```

- [ ] **Step 8: Run focused and full tray tests**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WorkLocationViewModelTests|FullyQualifiedName~PrepareWorkspaceViewModelTests|FullyQualifiedName~PhotoCaptureWindowViewModelTests"
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
```

Expected: all pass.

- [ ] **Step 9: Checkpoint the diff**

Visually inspect the setup and location screens. Commit as `feat: add work location confirmation to setup` only when authorized.

---

### Task 4: Clock-In mismatch warning, retry, cancel, and anyway flow

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/ClockInLocationContext.cs`
- Create: `ONEVO.Agent.TrayApp/Services/IUserNotificationService.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NotificationService.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeUserNotificationService.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`

**Interfaces:**
- Consumes: `ILocationService`, `IGeofenceEvaluator`, `IWorkLocationStore`.
- Produces: one pending `ClockInLocationVerification`, warning commands, and `ClockInLocationContext.Value` for the camera path.

- [ ] **Step 1: Write failing ViewModel tests for all decisions**

```csharp
[Fact]
public async Task ClockIn_MatchingFix_SendsImmediately()
{
    var fixture = ClockInFixture.Match();
    await fixture.Vm.ClockInCommand.ExecuteAsync(null);
    Assert.Single(fixture.Pipe.LifecycleLocations);
    Assert.Equal(LocationVerificationVerdict.Match,
        fixture.Pipe.LifecycleLocations[0]!.Verdict);
    Assert.False(fixture.Vm.IsLocationWarningVisible);
}

[Fact]
public async Task ClockIn_Mismatch_WarnsAndWaitsForDecision()
{
    var fixture = ClockInFixture.Mismatch();
    await fixture.Vm.ClockInCommand.ExecuteAsync(null);
    Assert.Empty(fixture.Pipe.LifecycleActions);
    Assert.True(fixture.Vm.IsLocationWarningVisible);
    Assert.Contains("away from", fixture.Vm.LocationWarningMessage);
    Assert.Single(fixture.Notifications.Warnings);
}

[Fact]
public async Task ClockInAnyway_SendsPendingMismatch()
{
    var fixture = ClockInFixture.Mismatch();
    await fixture.Vm.ClockInCommand.ExecuteAsync(null);
    await fixture.Vm.ClockInAnywayCommand.ExecuteAsync(null);
    Assert.Equal(LocationVerificationVerdict.Mismatch,
        Assert.Single(fixture.Pipe.LifecycleLocations)!.Verdict);
}

[Fact]
public async Task RetryLocation_ReplacesPendingAttempt()
{
    var fixture = ClockInFixture.MismatchThenMatch();
    await fixture.Vm.ClockInCommand.ExecuteAsync(null);
    var firstAttempt = fixture.Vm.PendingLocationAttemptId;
    await fixture.Vm.RetryLocationCommand.ExecuteAsync(null);
    Assert.NotEqual(firstAttempt, fixture.Pipe.LifecycleLocations.Single()!.AttemptId);
}

[Fact]
public async Task CancelLocationWarning_DoesNotClockIn()
{
    var fixture = ClockInFixture.Mismatch();
    await fixture.Vm.ClockInCommand.ExecuteAsync(null);
    fixture.Vm.CancelLocationWarningCommand.Execute(null);
    Assert.Empty(fixture.Pipe.LifecycleActions);
    Assert.False(fixture.Vm.IsLocationWarningVisible);
}
```

Also add cases for `Unavailable`, `Inaccurate`, and missing saved reference.

- [ ] **Step 2: Run the focused tests and verify red**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~ClockInViewModelTests
```

Expected: compile failures for new dependencies/properties.

- [ ] **Step 3: Add testable notification and in-memory context**

```csharp
public interface IUserNotificationService
{
    void ShowWarning(string title, string message);
}

public sealed class ClockInLocationContext
{
    public ClockInLocationVerification? Value { get; set; }
    public void Clear() => Value = null;
}
```

Make existing `NotificationService` implement the interface; preserve its current Windows toast implementation.

- [ ] **Step 4: Implement ClockIn orchestration**

`ClockInCommand` must:

1. require a saved reference;
2. request a new fix every invocation;
3. create `Unavailable` for capture failures;
4. evaluate valid fixes;
5. proceed immediately only for `Match`;
6. set exact warning copy and emit one Windows warning for other verdicts;
7. wait for Retry, Cancel, or Clock In Anyway before lifecycle submission.

Use a private `ContinueClockInAsync(ClockInLocationVerification verification, CancellationToken ct)` so the camera and non-camera branches share the same accepted attempt. If camera verification is enabled, save `ClockInLocationContext.Value` and route to `//photo?context=clockin`; otherwise send lifecycle immediately.

- [ ] **Step 5: Add the foreground warning panel**

In `ClockInPage.xaml`, bind a warning card to `IsLocationWarningVisible`. It contains:

- title and message bindings;
- `Retry Location` -> `RetryLocationCommand`;
- `Clock In Anyway` -> `ClockInAnywayCommand`;
- `Cancel` -> `CancelLocationWarningCommand`.

Disable the main Clock In button while location capture or a warning decision is pending.

- [ ] **Step 6: Register new services and run tests**

```csharp
builder.Services.AddSingleton<ClockInLocationContext>();
builder.Services.AddSingleton<IUserNotificationService>(sp =>
    sp.GetRequiredService<NotificationService>());
```

Run the Step 2 command and then the full tray test project. Expected: green.

- [ ] **Step 7: Checkpoint the diff**

Confirm that no timer or background collector calls `ILocationService`. Commit as `feat: verify location before clock in` only when authorized.

---

### Task 5: Carry the accepted location through face verification and IPC

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/LifecycleLocationSerializationTests.cs`

**Interfaces:**
- Consumes: Task 4's `ClockInLocationContext`.
- Produces: `LifecycleCommandPayload.LocationVerification` and matching client method parameter.

- [ ] **Step 1: Write failing camera hand-off and JSON round-trip tests**

```csharp
[Fact]
public async Task ClockInContext_PassesLocationToLifecycleAfterFaceCapture()
{
    var context = new ClockInLocationContext { Value = AVerification(LocationVerificationVerdict.Mismatch) };
    var pipe = new FakeNamedPipeClient();
    var vm = MakePhotoVm(pipe: pipe, clockInLocationContext: context);
    vm.SetContext("clockin");
    await vm.CapturePhotoCommand.ExecuteAsync(null);
    await vm.ContinueCommand.ExecuteAsync(null);
    Assert.Equal(context.Value, pipe.LifecycleLocations.Single());
}

[Fact]
public void LifecyclePayload_RoundTripsLocationVerification()
{
    var original = new LifecycleCommandPayload(
        LifecycleAction.ClockIn, null, AVerification(LocationVerificationVerdict.Match));
    var json = JsonSerializer.Serialize(original);
    var restored = JsonSerializer.Deserialize<LifecycleCommandPayload>(json);
    Assert.Equal(original.LocationVerification, restored!.LocationVerification);
}
```

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~LifecycleLocationSerializationTests|FullyQualifiedName~PhotoCaptureWindowViewModelTests"
```

- [ ] **Step 3: Extend IPC compatibly**

```csharp
public sealed record LifecycleCommandPayload(
    LifecycleAction Action,
    string? BreakReason = null,
    ClockInLocationVerification? LocationVerification = null);
```

Add the same optional final parameter to `INamedPipeClient.SendLifecycleAsync` and `NamedPipeClient.SendLifecycleAsync`. Existing break/clock-out call sites compile unchanged because the parameter defaults to `null`.

- [ ] **Step 4: Pass and clear the camera context**

On `context=clockin`, `PhotoCaptureWindowViewModel` passes `ClockInLocationContext.Value` to `SendLifecycleAsync`. Clear the context after success, lifecycle failure, cancellation/navigation away, and Sign Out. Use the current fix for the clock-in face photo's latitude/longitude; onboarding photo may use the saved reference.

- [ ] **Step 5: Run focused and full tray tests**

Use Step 2, then:

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
```

Expected: all pass.

- [ ] **Step 6: Checkpoint the diff**

Inspect serialized IPC JSON to confirm older payloads without location still deserialize. Commit as `feat: carry clock-in location through face verification` only when authorized.

---

### Task 6: Durable location audit record for server/admin notification

**Files:**
- Modify: `ONEVO.Agent.Shared/Models/CollectionRecord.cs`
- Create: `ONEVO.Agent.Service/Location/ClockInLocationRecordFactory.cs`
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Create: `tests/ONEVO.Agent.Service.Tests/Location/ClockInLocationRecordFactoryTests.cs`
- Modify: `tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs`

**Interfaces:**
- Consumes: `LifecycleCommandPayload.LocationVerification`.
- Produces: offline-safe `clock_in_location_verification` collection records.

- [ ] **Step 1: Write failing record-factory tests**

```csharp
[Fact]
public void Create_UsesAttemptIdAndExactSchema()
{
    var verification = AVerification(LocationVerificationVerdict.Mismatch);
    var record = ClockInLocationRecordFactory.Create(verification, "device-1");

    Assert.Equal(verification.AttemptId.ToString("N"), record.EventId);
    Assert.Equal("clock_in_location_verification", record.RecordType);
    Assert.Equal("1.0", record.SchemaVersion);
    Assert.Equal("device-1", record.DeviceId);
    Assert.Equal("Mismatch", record.Payload.GetProperty("verdict").GetString());
}

[Fact]
public void Create_PreservesDistanceAccuracyAndReference()
{
    var verification = AVerification(LocationVerificationVerdict.Match);
    var record = ClockInLocationRecordFactory.Create(verification, "device-1");
    Assert.True(record.Payload.TryGetProperty("currentFix", out _));
    Assert.True(record.Payload.TryGetProperty("reference", out _));
    Assert.True(record.Payload.TryGetProperty("effectiveRadiusMeters", out _));
}
```

- [ ] **Step 2: Run focused service tests and verify failure**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~ClockInLocationRecordFactoryTests
```

- [ ] **Step 3: Add record constants and factory**

```csharp
public const string ClockInLocationVerification = "clock_in_location_verification";
public const string ClockInLocationVerificationV1 = "1.0";
```

The factory sets event ID from `AttemptId`, capture timestamp from `CurrentFix.CapturedAt` or UTC now for unavailable fixes, and serializes the complete verification as payload.

- [ ] **Step 4: Queue only after successful Clock In**

In `HandleLifecycleCommandAsync`, after `ExecuteClockIn` returns success and only for `LifecycleAction.ClockIn`, create and `TryEnqueue` the record when `payload.LocationVerification` is non-null. Queue all verdicts so the server distinguishes confirmed match, mismatch, unavailable, and inaccurate. Log verdict and attempt ID; never log exact coordinates.

- [ ] **Step 5: Prove the existing buffer/sync accepts the record**

Add an `ActivitySyncServiceTests` case that enqueues the factory record, runs one sync batch, and asserts it is submitted and acknowledged exactly once. This verifies offline delivery and idempotency without a new HTTP endpoint.

- [ ] **Step 6: Run service tests**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~ClockInLocationRecordFactoryTests|FullyQualifiedName~ActivitySyncServiceTests"
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj
```

Expected: focused and full service suites pass.

- [ ] **Step 7: Checkpoint the diff**

Confirm that the record payload is the backend contract from the spec and that exact coordinates are absent from log messages. Commit as `feat: queue clock-in location audit events` only when authorized.

---

### Task 7: Sign-Out cleanup and regression coverage

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2's `SessionPreferenceKeys.ClearAll(IPreferencesStore)` and Task 4's context.
- Produces: guaranteed clean activation boundary.

- [ ] **Step 1: Write failing cleanup tests**

```csharp
[Fact]
public async Task SuccessfulSignOut_ClearsReferenceAndPendingClockInLocation()
{
    var fixture = ClockInFixture.Match();
    fixture.LocationStore.Save(AReference());
    fixture.Context.Value = AVerification(LocationVerificationVerdict.Match);

    await fixture.Vm.SignOutCommand.ExecuteAsync(null);

    Assert.Null(fixture.LocationStore.Load());
    Assert.Null(fixture.Context.Value);
}

[Fact]
public async Task FailedSignOut_PreservesReferenceForRetry()
{
    var fixture = ClockInFixture.WithLogoutFailure();
    fixture.LocationStore.Save(AReference());
    await fixture.Vm.SignOutCommand.ExecuteAsync(null);
    Assert.NotNull(fixture.LocationStore.Load());
}
```

Also assert a successful new activation clears any stale location reference before writing employee fields.

- [ ] **Step 2: Run focused tests and verify the intended failure**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~ClockInViewModelTests|FullyQualifiedName~ConnectWorkspaceViewModelTests"
```

- [ ] **Step 3: Complete cleanup through injected stores**

Call `SessionPreferenceKeys.ClearAll(_preferences)` only after successful service logout. Clear `ClockInLocationContext` at the same point. On activation success, clear stale session data before saving the new employee. Do not clear anything when logout fails.

- [ ] **Step 4: Run focused and full tray tests**

Use Step 2, then the full tray test command. Expected: all pass.

- [ ] **Step 5: Checkpoint the diff**

Confirm completed attendance history and installation device identity remain untouched. Commit as `fix: clear location state on employee sign out` only when authorized.

---

### Task 8: Full verification and visual acceptance

**Files:**
- Verify only; modify production files only for defects found by the checks.

**Interfaces:**
- Consumes: all previous tasks.
- Produces: evidence that the complete location workflow works without regressions.

- [ ] **Step 1: Run both full test projects**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj
```

Expected: zero failed tests.

- [ ] **Step 2: Build the Windows tray app**

```powershell
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Manually verify onboarding**

At the supported minimum window size:

1. activate an employee;
2. confirm Setup shows Work Location above Profile Picture;
3. deny permission once and verify the explicit recovery message;
4. enable permission and Refresh;
5. choose each of the three location types and confirm exact labels;
6. confirm location, capture face, and verify Continue becomes enabled;
7. verify no clipped title, cards, action buttons, or footer.

- [ ] **Step 4: Manually verify Clock In outcomes**

Using a fake/debug location provider or injected fixture:

1. in-radius fix -> no warning, normal Clock In;
2. out-of-radius fix -> in-app warning + Windows toast, no lifecycle send yet;
3. Retry -> a new attempt ID/fix;
4. Cancel -> remain Ready;
5. Clock In Anyway -> normal camera/lifecycle path and one queued mismatch record;
6. poor accuracy -> `Inaccurate`, not `Mismatch`;
7. offline service sync -> record remains pending and later acknowledges once.

- [ ] **Step 5: Verify privacy and cleanup**

Confirm no background timer requests location, coordinate values are absent from logs, and successful Sign Out removes the saved reference and pending context.

- [ ] **Step 6: Review the final diff**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intended files are part of this feature. Request code review before declaring implementation complete.

---

## Self-Review Result

- Spec coverage: setup capture, three options, fresh Clock In capture, match/mismatch/accuracy rules, warning actions, camera hand-off, IPC, durable sync, and Sign Out cleanup each map to an explicit task.
- Placeholder scan: no deferred implementation steps or unspecified error handling remain.
- Type consistency: every later task uses the exact Task 1 names `GeoLocationFix`, `WorkLocationReference`, `ClockInLocationVerification`, and `LocationVerificationVerdict`; lifecycle and storage boundaries use those same contracts.
- Scope boundary: tray employee warnings and durable server events are implemented here; backend admin-dashboard rendering remains outside this repository as stated by the spec.
