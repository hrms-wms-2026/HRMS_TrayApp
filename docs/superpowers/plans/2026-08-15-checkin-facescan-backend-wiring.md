# CheckIn + FaceScan Backend Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing MAUI face-photo capture and GPS location UI to the existing backend `/api/v1/monitoring/check-in` and `/{checkInId}/face-scan` APIs so that a completed clock-in flow actually persists attendance data in the backend.

**Architecture:** `WorkLocationViewModel.SaveAndContinue()` writes GPS to `IPreferencesStore` as strings. `PhotoCaptureWindowViewModel.Continue()` reads GPS from `IPreferencesStore`, embeds it in the `FacePhotoPayload`, and sends the record over IPC to the Service. `ActivitySyncService.FlushAsync()` picks up the `FacePhoto` record, calls the backend `POST /check-in` to create the attendance row, then uploads the photo to `POST /check-in/{id}/face-scan` as multipart.

**Tech Stack:** C# / .NET 10, MAUI (TrayApp side), ASP.NET Core (backend), xUnit, `IHttpClientFactory`, `MultipartFormDataContent`, `System.Text.Json`.

---

## Why It's Broken Now (Read First)

| Gap | Location | Effect |
|-----|----------|--------|
| GPS stored as `double` via raw `Preferences.Set(key, double)` | `WorkLocationViewModel.SaveAndContinue()` | `IPreferencesStore` (string-only) can't read it back |
| `FacePhotoPayload` has no GPS fields | `PhotoCaptureWindowViewModel.Continue()` | No location data in the IPC record |
| `ActivitySyncService.FlushAsync` has no `FacePhoto` branch | `ActivitySyncService.cs` | Record hits `LogWarning("Unknown record type")` and is skipped forever |
| No backend routes for CheckIn/FaceScan | `AgentApiRoutes.cs` | Service has no URL to call even if the above were fixed |
| No request/response models for CheckIn/FaceScan | `ActivityIngestModels.cs` | Can't serialize request or deserialize `check_in_id` from response |

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `ONEVO.Agent.Shared/Models/FacePhotoPayload.cs` | **Create** | Payload model shared between TrayApp and Service |
| `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs` | **Modify** | Inject `IPreferencesStore`, write GPS as strings |
| `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs` | **Modify** | Inject `IPreferencesStore`, embed GPS in payload |
| `ONEVO.Agent.Service/Api/AgentApiRoutes.cs` | **Modify** | Add CheckIn + FaceScan route constants |
| `ONEVO.Agent.Service/Api/ActivityIngestModels.cs` | **Modify** | Add `CheckInSubmitRequest` / `CheckInSubmitResponse` |
| `ONEVO.Agent.Service/Sync/ActivitySyncService.cs` | **Modify** | Handle `FacePhoto` record in `FlushAsync`, add `FlushFacePhotoAsync` |
| `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs` | **Modify** | Add GPS-written-to-prefs test |
| `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs` | **Modify** | Add GPS-embedded-in-record test |
| `tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs` | **Modify** | Add FacePhoto flush tests |

---

## Task 1: Add `FacePhotoPayload` model to Shared

**Files:**
- Create: `ONEVO.Agent.Shared/Models/FacePhotoPayload.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace ONEVO.Agent.Shared.Models;

public sealed record FacePhotoPayload
{
    public required string Format { get; init; }
    public required string Data { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? LocationAccuracy { get; init; }
    public string? LocationAddress { get; init; }
}
```

- [ ] **Step 2: Build Shared to confirm no errors**

```
cd C:\HR\tray_app_maui
dotnet build ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj -c Debug --nologo
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add ONEVO.Agent.Shared/Models/FacePhotoPayload.cs
git commit -m "feat(shared): add FacePhotoPayload with GPS fields"
```

---

## Task 2: Write GPS as strings via `IPreferencesStore` in `WorkLocationViewModel`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`

**Why this is needed:** `IPreferencesStore` is string-only. MAUI `Preferences.Set(key, double)` uses a type-specific storage slot that `IPreferencesStore.Get(key, "")` cannot read. By switching to `_prefs.Set(key, la.ToString("G17"))` the GPS coordinates are readable as strings by any ViewModel holding the same `IPreferencesStore` instance.

- [ ] **Step 1: Write the failing test**

Add to `WorkLocationViewModelTests.cs` (at the bottom of the class, before the last `}`):

```csharp
[Fact]
public async Task SaveAndContinue_WithLiveFix_WritesGpsStringsToPrefs()
{
    var prefs = new FakePreferencesStore();
    var loc   = new FixedLocationService(new GeoPoint(13.0827, 80.2707));
    var vm    = new WorkLocationViewModel(loc, prefs);

    await vm.DetectLiveLocationCommand.ExecuteAsync(null);
    vm.SelectedLocation = vm.ApprovedLocations[0];
    await vm.SaveAndContinueCommand.ExecuteAsync(null);

    Assert.Equal("13.082700000000001", prefs.Get("onevo.live_latitude",  ""));
    Assert.Equal("80.27070000000001",  prefs.Get("onevo.live_longitude", ""));
}
```

> Note: `G17` round-trips `double` exactly. The asserted strings match `(13.0827).ToString("G17")` and `(80.2707).ToString("G17")`.

- [ ] **Step 2: Run the test to confirm it fails**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --filter "SaveAndContinue_WithLiveFix_WritesGpsStringsToPrefs" --nologo
```
Expected: FAIL — `WorkLocationViewModel` constructor does not accept `IPreferencesStore` yet.

- [ ] **Step 3: Modify `WorkLocationViewModel`**

In `WorkLocationViewModel.cs`, make these changes:

**Add field and update constructor:**

Replace:
```csharp
public WorkLocationViewModel(ILocationService location)
{
    Title = "Select Your Work Location";
    _location = location;
}

/// <summary>Parameterless for existing unit tests — uses a no-op location source.</summary>
public WorkLocationViewModel() : this(new NullLocationService()) { }
```

With:
```csharp
private readonly IPreferencesStore _prefs;

public WorkLocationViewModel(ILocationService location, IPreferencesStore prefs)
{
    Title = "Select Your Work Location";
    _location = location;
    _prefs = prefs;
}

/// <summary>Parameterless for existing unit tests — uses a no-op location source.</summary>
public WorkLocationViewModel() : this(new NullLocationService(), new PreferencesStore()) { }
```

**Update `SaveAndContinue()` — replace the `Preferences.Set` calls with `_prefs.Set`:**

Replace:
```csharp
[RelayCommand(CanExecute = nameof(HasSelection))]
private async Task SaveAndContinue()
{
    try
    {
        Preferences.Set("onevo.work_location_code",    SelectedLocation!.Code);
        Preferences.Set("onevo.work_location_display", SelectedLocation.DisplayName);
        if (LiveLatitude is { } la && LiveLongitude is { } lo)
        {
            Preferences.Set("onevo.live_latitude",  la);
            Preferences.Set("onevo.live_longitude", lo);
        }
    }
    catch { /* no MAUI Preferences host in unit tests */ }

    try { await Shell.Current.GoToAsync("//photo"); }
    catch { /* unit tests */ }
}
```

With:
```csharp
[RelayCommand(CanExecute = nameof(HasSelection))]
private async Task SaveAndContinue()
{
    _prefs.Set("onevo.work_location_code",    SelectedLocation!.Code);
    _prefs.Set("onevo.work_location_display", SelectedLocation.DisplayName);
    if (LiveLatitude is { } la && LiveLongitude is { } lo)
    {
        _prefs.Set("onevo.live_latitude",  la.ToString("G17"));
        _prefs.Set("onevo.live_longitude", lo.ToString("G17"));
    }

    try { await Shell.Current.GoToAsync("//photo"); }
    catch { /* unit tests */ }
}
```

- [ ] **Step 4: Run the test to confirm it passes**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --filter "SaveAndContinue_WithLiveFix_WritesGpsStringsToPrefs" --nologo
```
Expected: PASS

- [ ] **Step 5: Run the full TrayApp test suite to confirm no regressions**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --nologo
```
Expected: All previously passing tests still pass.

- [ ] **Step 6: Commit**

```
git add ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs
git commit -m "feat(trayapp): store GPS as strings in IPreferencesStore from WorkLocationViewModel"
```

---

## Task 3: Embed GPS in `FacePhotoPayload` inside `PhotoCaptureWindowViewModel`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Open `PhotoCaptureWindowViewModelTests.cs`. At the top, add the needed usings if not present:
```csharp
using System.Text.Json;
using ONEVO.Agent.Shared.Models;
```

Add this test to the class:

```csharp
[Fact]
public async Task Continue_EmbedsCapturedGpsFromPrefsIntoFacePhotoRecord()
{
    var prefs = new FakePreferencesStore();
    prefs.Set("onevo.live_latitude",     "13.082700000000001");
    prefs.Set("onevo.live_longitude",    "80.27070000000001");
    prefs.Set("onevo.work_location_display", "Chennai Office");

    var pipe = new FakeNamedPipeClient();
    var vm   = new PhotoCaptureWindowViewModel(
        new FakeCameraService { ShouldReturnPhoto = true }, pipe, prefs);

    await vm.CapturePhotoCommand.ExecuteAsync(null);
    await vm.ContinueCommand.ExecuteAsync(null);

    var submitted = Assert.Single(pipe.Submitted);
    var record    = Assert.Single(submitted);
    Assert.Equal(CollectionRecordTypes.FacePhoto, record.RecordType);

    var payload = record.Payload.Deserialize<FacePhotoPayload>()!;
    Assert.NotNull(payload.Latitude);
    Assert.NotNull(payload.Longitude);
    Assert.InRange(payload.Latitude!.Value,  13.08, 13.09);
    Assert.InRange(payload.Longitude!.Value, 80.27, 80.28);
    Assert.Equal("Chennai Office", payload.LocationAddress);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --filter "Continue_EmbedsCapturedGpsFromPrefsIntoFacePhotoRecord" --nologo
```
Expected: FAIL — constructor does not accept `IPreferencesStore` yet.

- [ ] **Step 3: Modify `PhotoCaptureWindowViewModel`**

**Add field and update constructor:**

Replace:
```csharp
public PhotoCaptureWindowViewModel(ICameraService camera, INamedPipeClient pipe)
{
    Title   = "Face Verification";
    _camera = camera;
    _pipe   = pipe;
}
```

With:
```csharp
private readonly IPreferencesStore _prefs;

public PhotoCaptureWindowViewModel(ICameraService camera, INamedPipeClient pipe, IPreferencesStore prefs)
{
    Title   = "Face Verification";
    _camera = camera;
    _pipe   = pipe;
    _prefs  = prefs;
}
```

**Update `Continue()` — replace the anonymous payload object with `FacePhotoPayload`:**

Replace:
```csharp
if (_capturedBytes is { Length: > 0 })
{
    try
    {
        var payload = new { format = "jpeg", data = Convert.ToBase64String(_capturedBytes) };
        var record  = new CollectionRecord
        {
            EventId          = Guid.NewGuid().ToString("N"),
            RecordType       = CollectionRecordTypes.FacePhoto,
            SchemaVersion    = CollectionSchemaVersions.FacePhotoV1,
            CaptureTimestamp = DateTimeOffset.UtcNow,
            DeviceId         = Environment.MachineName,
            Payload          = JsonSerializer.SerializeToElement(payload)
        };
        await _pipe.SubmitCollectionRecordsAsync([record], CancellationToken.None);
    }
    catch { /* non-blocking — photo send failure should not block navigation */ }
}
```

With:
```csharp
if (_capturedBytes is { Length: > 0 })
{
    try
    {
        double? lat = double.TryParse(
            _prefs.Get("onevo.live_latitude", ""),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var la) ? la : null;
        double? lon = double.TryParse(
            _prefs.Get("onevo.live_longitude", ""),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var lo) ? lo : null;
        var locationDisplay = _prefs.Get("onevo.work_location_display", "");

        var payload = new FacePhotoPayload
        {
            Format          = "jpeg",
            Data            = Convert.ToBase64String(_capturedBytes),
            Latitude        = lat,
            Longitude       = lon,
            LocationAddress = string.IsNullOrEmpty(locationDisplay) ? null : locationDisplay
        };
        var record = new CollectionRecord
        {
            EventId          = Guid.NewGuid().ToString("N"),
            RecordType       = CollectionRecordTypes.FacePhoto,
            SchemaVersion    = CollectionSchemaVersions.FacePhotoV1,
            CaptureTimestamp = DateTimeOffset.UtcNow,
            DeviceId         = Environment.MachineName,
            Payload          = JsonSerializer.SerializeToElement(payload)
        };
        await _pipe.SubmitCollectionRecordsAsync([record], CancellationToken.None);
    }
    catch { /* non-blocking — photo send failure should not block navigation */ }
}
```

**Add the missing `using` at the top of `PhotoCaptureWindowViewModel.cs`** if not present:
```csharp
using ONEVO.Agent.TrayApp.Services;
```

- [ ] **Step 4: Run the new test to confirm it passes**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --filter "Continue_EmbedsCapturedGpsFromPrefsIntoFacePhotoRecord" --nologo
```
Expected: PASS

- [ ] **Step 5: Run the full TrayApp suite**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --nologo
```
Expected: All passing. (Existing `PhotoCaptureWindowViewModelTests` tests will fail if they call the 2-arg constructor — update the `MakeVm` helper in that file.)

**Fix `MakeVm` in `PhotoCaptureWindowViewModelTests.cs`** if any tests break:

Replace:
```csharp
private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true) =>
    new(new FakeCameraService { ShouldReturnPhoto = cameraSucceeds }, new FakeNamedPipeClient());
```

With:
```csharp
private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true) =>
    new(new FakeCameraService { ShouldReturnPhoto = cameraSucceeds },
        new FakeNamedPipeClient(),
        new FakePreferencesStore());
```

- [ ] **Step 6: Confirm all tests pass**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Debug --nologo
```
Expected: All passing.

- [ ] **Step 7: Commit**

```
git add ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs
git commit -m "feat(trayapp): embed GPS coords in FacePhotoPayload before IPC submit"
```

---

## Task 4: Add CheckIn / FaceScan routes and request/response models to Service

**Files:**
- Modify: `ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Modify: `ONEVO.Agent.Service/Api/ActivityIngestModels.cs`

No new tests for pure constants/models — they are exercised by the Service tests in Task 5.

- [ ] **Step 1: Add routes to `AgentApiRoutes.cs`**

Open `ONEVO.Agent.Service/Api/AgentApiRoutes.cs`. Add these two lines after `WorkSessionSubmit`:

```csharp
public const string CheckInSubmit   = "/api/v1/monitoring/check-in";
public const string FaceScanUpload  = "/api/v1/monitoring/check-in/{0}/face-scan";
```

The resulting routes block should look like:
```csharp
public const string ActivitySnapshots    = "/api/v1/monitoring/activity/snapshots";
public const string AppUsageSnapshots    = "/api/v1/monitoring/app-usage/snapshots";
public const string DeviceStateSnapshots = "/api/v1/monitoring/device-state/snapshots";
public const string WorkSessionSubmit    = "/api/v1/monitoring/work-sessions";
public const string CheckInSubmit        = "/api/v1/monitoring/check-in";
public const string FaceScanUpload       = "/api/v1/monitoring/check-in/{0}/face-scan";
public const string ScreenshotSubmit     = "/api/v1/monitoring/tray/screenshots";
public const string InactivityAttemptSubmit = "/api/v1/monitoring/tray/inactivity-attempts";
public const string TrayPolicy           = "/api/v1/monitoring/tray/policy";
```

- [ ] **Step 2: Add request/response models to `ActivityIngestModels.cs`**

Append to the end of `ONEVO.Agent.Service/Api/ActivityIngestModels.cs`:

```csharp
/// <summary>Wire format for POST /api/v1/monitoring/check-in.</summary>
public sealed class CheckInSubmitRequest
{
    [JsonPropertyName("latitude")]          public double? Latitude          { get; set; }
    [JsonPropertyName("longitude")]         public double? Longitude         { get; set; }
    [JsonPropertyName("location_accuracy")] public double? LocationAccuracy  { get; set; }
    [JsonPropertyName("location_address")]  public string? LocationAddress   { get; set; }
    [JsonPropertyName("device_serial_number")] public string? DeviceSerialNumber { get; set; }
}

/// <summary>Relevant fields from the check-in response — only check_in_id is needed for the face scan upload.</summary>
public sealed class CheckInSubmitResponse
{
    [JsonPropertyName("check_in_id")]      public Guid CheckInId      { get; set; }
    [JsonPropertyName("face_scan_required")] public bool FaceScanRequired { get; set; }
}
```

- [ ] **Step 3: Build Service to verify no errors**

```
cd C:\HR\tray_app_maui
dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj -c Debug --nologo
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add ONEVO.Agent.Service/Api/AgentApiRoutes.cs
git add ONEVO.Agent.Service/Api/ActivityIngestModels.cs
git commit -m "feat(service): add CheckIn + FaceScan route constants and request/response models"
```

---

## Task 5: Handle `FacePhoto` in `ActivitySyncService`

**Files:**
- Modify: `ONEVO.Agent.Service/Sync/ActivitySyncService.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs`

This is the core wiring task. `FlushAsync` currently hits a `LogWarning("Unknown record type")` for `FacePhoto` and increments `index` without acknowledging. We add a branch that calls `FlushFacePhotoAsync`.

The two-call HTTP flow inside `FlushFacePhotoAsync`:
1. `POST /api/v1/monitoring/check-in` → JSON body → response contains `check_in_id`
2. `POST /api/v1/monitoring/check-in/{checkInId}/face-scan` → multipart with `face_scan` file field

If either call fails with a network error or 5xx → re-queue (retry next flush cycle).  
If either call fails with 4xx (bad payload, policy rejected) → quarantine (drop the record).  
If CheckIn succeeds but FaceScan fails → quarantine entire record (the orphaned CheckIn row is acceptable for Phase 1; no retry logic needed for partial failures).

- [ ] **Step 1: Write the failing tests**

Add these three tests to `ActivitySyncServiceTests.cs` (before the final `}` of the class):

```csharp
[Fact]
public async Task FlushAsync_FacePhotoRecord_PostsCheckInThenFaceScan()
{
    var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    var payload = new FacePhotoPayload
    {
        Format    = "jpeg",
        Data      = Convert.ToBase64String(imageBytes),
        Latitude  = 13.0827,
        Longitude = 80.2707,
        LocationAddress = "Chennai Office"
    };
    var buffer = ActivityRecordBuffer.CreateInMemory();
    buffer.TryEnqueue(MakeRecord(
        CollectionRecordTypes.FacePhoto,
        CollectionSchemaVersions.FacePhotoV1,
        payload));

    var callOrder = new List<string>();
    var factory = new CapturingHttpClientFactory(req =>
    {
        if (req.RequestUri!.AbsolutePath.EndsWith("/check-in", StringComparison.Ordinal)
            && req.Method == HttpMethod.Post
            && req.Content?.Headers.ContentType?.MediaType == "application/json")
        {
            callOrder.Add("checkin");
            var checkInBody = JsonSerializer.Serialize(new
            {
                check_in_id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                face_scan_required = true
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(checkInBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
        callOrder.Add("facescan");
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    WithJwt(credentials =>
    {
        var svc = Build(buffer, factory, credentials: credentials);
        svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    Assert.Equal(["checkin", "facescan"], callOrder);
    Assert.Equal(0, buffer.Count);
}

[Fact]
public async Task FlushAsync_FacePhotoRecord_CheckInFails5xx_RequeuesRecord()
{
    var payload = new FacePhotoPayload
    {
        Format = "jpeg",
        Data   = Convert.ToBase64String(new byte[] { 1, 2, 3 })
    };
    var buffer = ActivityRecordBuffer.CreateInMemory();
    buffer.TryEnqueue(MakeRecord(
        CollectionRecordTypes.FacePhoto,
        CollectionSchemaVersions.FacePhotoV1,
        payload));

    var factory = new CapturingHttpClientFactory(
        _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

    WithJwt(credentials =>
    {
        var svc = Build(buffer, factory, credentials: credentials);
        svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    Assert.Equal(1, buffer.Count);
}

[Fact]
public async Task FlushAsync_FacePhotoRecord_CheckInFails4xx_QuarantinesRecord()
{
    var payload = new FacePhotoPayload
    {
        Format = "jpeg",
        Data   = Convert.ToBase64String(new byte[] { 1, 2, 3 })
    };
    var buffer = ActivityRecordBuffer.CreateInMemory();
    buffer.TryEnqueue(MakeRecord(
        CollectionRecordTypes.FacePhoto,
        CollectionSchemaVersions.FacePhotoV1,
        payload));

    var factory = new CapturingHttpClientFactory(
        _ => new HttpResponseMessage(HttpStatusCode.BadRequest));

    WithJwt(credentials =>
    {
        var svc = Build(buffer, factory, credentials: credentials);
        svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    Assert.Equal(0, buffer.Count);
}
```

You also need to add these usings at the top of `ActivitySyncServiceTests.cs` if not present:
```csharp
using ONEVO.Agent.Shared.Models;
```

- [ ] **Step 2: Run the tests to confirm they fail**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.Service.Tests -c Debug --filter "FacePhoto" --nologo
```
Expected: FAIL (the `FacePhoto` branch doesn't exist yet).

- [ ] **Step 3: Add the FacePhoto branch in `FlushAsync`**

In `ActivitySyncService.cs`, inside `FlushAsync`, find the block that currently ends with:

```csharp
_logger.LogWarning("Unknown record type {RecordType} — skipping row {RowId}", recordType, current.RowId);
index++;
```

Add the FacePhoto branch immediately BEFORE that warning block:

```csharp
if (recordType == CollectionRecordTypes.FacePhoto)
{
    var outcome = await FlushFacePhotoAsync(current, jwt, ct);
    switch (outcome)
    {
        case FacePhotoFlushOutcome.Acknowledged:
            acknowledged.Add(current.RowId);
            index++;
            continue;
        case FacePhotoFlushOutcome.Quarantined:
            index++;
            continue;
        default:
            break;
    }
    break;
}
```

- [ ] **Step 4: Add the enum and `FlushFacePhotoAsync` method**

Add this enum inside `ActivitySyncService` (alongside `InactivityFlushOutcome`):

```csharp
private enum FacePhotoFlushOutcome
{
    Acknowledged,
    Quarantined,
    RetryableFailure
}
```

Add this method to `ActivitySyncService` (after `FlushScreenshotsAsync`):

```csharp
private async Task<FacePhotoFlushOutcome> FlushFacePhotoAsync(
    BufferedCollectionRecord buffered, string jwt, CancellationToken ct)
{
    FacePhotoPayload photo;
    byte[] imageBytes;
    try
    {
        var parsed = buffered.Record.Payload.Deserialize<FacePhotoPayload>(JsonOptions);
        if (parsed is null)
        {
            _logger.LogWarning("Corrupt face photo payload quarantined eventId={EventId}", buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
            return FacePhotoFlushOutcome.Quarantined;
        }
        photo = parsed;
        imageBytes = Convert.FromBase64String(photo.Data);
    }
    catch (Exception ex) when (ex is JsonException or FormatException)
    {
        _logger.LogWarning(ex, "Corrupt face photo record quarantined eventId={EventId}", buffered.Record.EventId);
        _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
        return FacePhotoFlushOutcome.Quarantined;
    }

    // Step 1: Submit check-in to get checkInId
    var checkInRequest = new CheckInSubmitRequest
    {
        Latitude        = photo.Latitude,
        Longitude       = photo.Longitude,
        LocationAccuracy = photo.LocationAccuracy,
        LocationAddress  = photo.LocationAddress,
        DeviceSerialNumber = null
    };

    var client = _httpClientFactory.CreateClient("OnevoApi");
    using var checkInHttpRequest = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.CheckInSubmit)
    {
        Content = JsonContent.Create(checkInRequest)
    };
    checkInHttpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

    HttpResponseMessage checkInResponse;
    try
    {
        checkInResponse = await client.SendAsync(checkInHttpRequest, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "HTTP failed for check-in eventId={EventId}", buffered.Record.EventId);
        return FacePhotoFlushOutcome.RetryableFailure;
    }

    using (checkInResponse)
    {
        if ((int)checkInResponse.StatusCode >= 400 && (int)checkInResponse.StatusCode < 500)
        {
            _logger.LogWarning(
                "Check-in rejected status={Status} eventId={EventId} — quarantining",
                (int)checkInResponse.StatusCode, buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "checkin_rejected");
            return FacePhotoFlushOutcome.Quarantined;
        }

        if (checkInResponse.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
        {
            _logger.LogWarning(
                "Check-in failed status={Status} eventId={EventId} — will retry",
                (int)checkInResponse.StatusCode, buffered.Record.EventId);
            return FacePhotoFlushOutcome.RetryableFailure;
        }

        CheckInSubmitResponse? checkInBody;
        try
        {
            checkInBody = await checkInResponse.Content
                .ReadFromJsonAsync<CheckInSubmitResponse>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse check-in response eventId={EventId}", buffered.Record.EventId);
            return FacePhotoFlushOutcome.RetryableFailure;
        }

        if (checkInBody is null)
        {
            _logger.LogWarning("Null check-in response body eventId={EventId}", buffered.Record.EventId);
            return FacePhotoFlushOutcome.RetryableFailure;
        }

        // Step 2: Upload face scan
        var faceScanUrl = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            AgentApiRoutes.FaceScanUpload,
            checkInBody.CheckInId);

        using var faceScanContent = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        faceScanContent.Add(imageContent, "face_scan", $"{buffered.Record.EventId}.jpg");

        using var faceScanRequest = new HttpRequestMessage(HttpMethod.Post, faceScanUrl)
        {
            Content = faceScanContent
        };
        faceScanRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage faceScanResponse;
        try
        {
            faceScanResponse = await client.SendAsync(faceScanRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "HTTP failed for face scan upload checkInId={CheckInId} eventId={EventId}",
                checkInBody.CheckInId, buffered.Record.EventId);
            // CheckIn already committed in backend — quarantine to avoid duplicate check-in on retry
            _buffer.QuarantineRow(buffered.RowId, "facescan_upload_failed");
            return FacePhotoFlushOutcome.Quarantined;
        }

        using (faceScanResponse)
        {
            if (faceScanResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted)
            {
                _logger.LogInformation(
                    "Face photo accepted checkInId={CheckInId} eventId={EventId}",
                    checkInBody.CheckInId, buffered.Record.EventId);
                return FacePhotoFlushOutcome.Acknowledged;
            }

            _logger.LogWarning(
                "Face scan upload failed status={Status} checkInId={CheckInId} eventId={EventId} — quarantining",
                (int)faceScanResponse.StatusCode, checkInBody.CheckInId, buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "facescan_rejected");
            return FacePhotoFlushOutcome.Quarantined;
        }
    }
}
```

**Add the missing `using` at the top of `ActivitySyncService.cs`** if not present:
```csharp
using ONEVO.Agent.Shared.Models;
```

The `ReadFromJsonAsync<T>` overload that accepts `JsonSerializerOptions` (not `JsonTypeInfo`) requires the signature:
```csharp
await response.Content.ReadFromJsonAsync<CheckInSubmitResponse>(JsonOptions, ct)
```
This matches `HttpContentJsonExtensions.ReadFromJsonAsync<T>(HttpContent, JsonSerializerOptions?, CancellationToken)` which is available in `System.Net.Http.Json`.

Confirm the `using System.Net.Http.Json;` directive is present in the file (check top of `ActivitySyncService.cs`). If not, add it.

- [ ] **Step 5: Run the new tests to confirm they pass**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.Service.Tests -c Debug --filter "FacePhoto" --nologo
```
Expected: All 3 FacePhoto tests PASS.

- [ ] **Step 6: Run the full Service test suite**

```
cd C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.Service.Tests -c Debug --nologo
```
Expected: All passing. No regressions.

- [ ] **Step 7: Full build check across all projects**

```
cd C:\HR\tray_app_maui
dotnet build ONEVO.Agent.slnx -c Debug --nologo
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**

```
git add ONEVO.Agent.Service/Sync/ActivitySyncService.cs
git add tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs
git commit -m "feat(service): wire FacePhoto record to backend CheckIn + FaceScan upload"
```

---

## End-to-End Smoke Test (Manual)

After all tasks are committed, do a live run:

1. Start backend: `cd C:\HR\HRMS-Backend-v1\src\ONEVO.Api && dotnet run`
2. Start Service: `cd C:\HR\tray_app_maui && $env:DOTNET_ENVIRONMENT="Development" && dotnet run --project ONEVO.Agent.Service/ONEVO.Agent.Service.csproj -c Debug`
3. Open TrayApp (installed MSIX) → go through Location page → Photo page → Capture + Continue
4. Watch Service log — expect to see:
   ```
   [INFO] Face photo accepted checkInId=<guid> eventId=<id>
   ```
5. In backend Swagger (`https://localhost:7229/swagger`) or DB, verify a new `EmployeeCheckIn` row and `MonitoringFaceScan` row exist.

---

## Self-Review Checklist

- [x] **Spec coverage:** GPS in payload ✓, CheckIn API call ✓, FaceScan API call ✓, retry/quarantine error handling ✓, tests for each ✓
- [x] **No placeholders:** All code blocks are complete and compilable
- [x] **Type consistency:** `FacePhotoPayload` defined in Task 1, used identically in Tasks 3 and 5. `CheckInSubmitRequest`/`CheckInSubmitResponse` defined in Task 4, used in Task 5. `AgentApiRoutes.CheckInSubmit`/`FaceScanUpload` defined in Task 4, used in Task 5.
- [x] **DeviceId note:** The `DeviceId = Environment.MachineName` in the FacePhoto record is intentionally left as-is — the backend authenticates via JWT (`ITrayCurrentDevice`), not via the record's DeviceId field. Fixing the DeviceId stamping (to use the enrolled `DeviceIdentityStore.Load()?.DeviceId`) is a separate cleanup task.
- [x] **Partial failure:** If CheckIn succeeds but FaceScan HTTP errors → quarantine (not retry) to avoid creating a second duplicate `EmployeeCheckIn` row on retry. This is documented in the code comment.
