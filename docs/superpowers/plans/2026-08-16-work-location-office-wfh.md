# Work Location: Simplify to Office / Work From Home Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the four-option Work Location picker (Chennai Office / Bangalore Office / Hyderabad Office / Work From Home) into a generic two-option picker (Office / Work From Home), still auto-selected from live GPS via the existing 80 km geofence threshold.

**Architecture:** `WorkLocationViewModel` keeps the three office coordinates as a private, non-displayed geofence table used only to decide Office vs. WFH. `ApprovedLocations` (bound to the UI) shrinks to two generic `WorkLocationOption` entries. `FindNearestOffice` returns which of the two to select plus the matched city name/distance for the status banner only — never persisted. The search box is removed since two fixed options don't need filtering.

**Tech Stack:** .NET MAUI (C#, XAML), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-16-work-location-office-wfh-design.md`

---

### Task 1: Update the ViewModel unit tests for the two-option model

**Files:**
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`

- [ ] **Step 1: Replace the whole test file with the two-option version**

Replace the entire contents of `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs` with:

```csharp
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class WorkLocationViewModelTests
{
    private sealed class FixedLocationService : ILocationService
    {
        private readonly GeoPoint? _point;
        public FixedLocationService(GeoPoint? point) => _point = point;
        public Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(_point);
    }

    [Fact]
    public void ApprovedLocations_HasTwoEntries()
    {
        var vm = new WorkLocationViewModel();
        Assert.Equal(2, vm.ApprovedLocations.Count);
    }

    [Fact]
    public void ApprovedLocations_ContainsOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Office" && l.Code == "OFFICE");
    }

    [Fact]
    public void ApprovedLocations_ContainsWorkFromHome()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations,
            l => l.DisplayName == "Work From Home" && l.Code == "WFH");
    }

    [Fact]
    public void FindNearestOffice_NearChennai_SelectsOffice()
    {
        var vm = new WorkLocationViewModel();
        // ~T.Nagar, Chennai
        var match = vm.FindNearestOffice(13.0418, 80.2341);
        Assert.NotNull(match);
        Assert.Equal("OFFICE", match!.Option.Code);
        Assert.Equal("Chennai", match.NearestCity);
        Assert.False(match.IsRemoteFallback);
        Assert.True(match.DistanceKm < 20);
    }

    [Fact]
    public void FindNearestOffice_NearBangalore_SelectsOffice()
    {
        var vm = new WorkLocationViewModel();
        var match = vm.FindNearestOffice(12.9716, 77.5946);
        Assert.NotNull(match);
        Assert.Equal("OFFICE", match!.Option.Code);
        Assert.Equal("Bangalore", match.NearestCity);
        Assert.False(match.IsRemoteFallback);
    }

    [Fact]
    public void FindNearestOffice_FarFromOffices_SuggestsWfh()
    {
        var vm = new WorkLocationViewModel();
        // London
        var match = vm.FindNearestOffice(51.5074, -0.1278);
        Assert.NotNull(match);
        Assert.Equal("WFH", match!.Option.Code);
        Assert.True(match.IsRemoteFallback);
    }

    [Fact]
    public async Task DetectLiveLocation_NearOffice_AutoSelectsOffice()
    {
        // Approximate Bangalore CBD
        var loc = new FixedLocationService(new GeoPoint(12.9716, 77.5946));
        var vm = new WorkLocationViewModel(loc);
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedLocation);
        Assert.Equal("OFFICE", vm.SelectedLocation!.Code);
        Assert.Contains("Office selected", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsDetectingLocation);
    }

    [Fact]
    public async Task DetectLiveLocation_FarFromOffice_AutoSelectsWfh()
    {
        // London
        var loc = new FixedLocationService(new GeoPoint(51.5074, -0.1278));
        var vm = new WorkLocationViewModel(loc);
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedLocation);
        Assert.Equal("WFH", vm.SelectedLocation!.Code);
        Assert.Contains("Work From Home selected", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectLiveLocation_WhenUnavailable_DoesNotForceSelection()
    {
        var vm = new WorkLocationViewModel(new FixedLocationService(null));
        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedLocation);
        Assert.Contains("Could not get live location", vm.LiveLocationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveAndContinueCommand_DisabledWhenNoSelection()
    {
        var vm = new WorkLocationViewModel();
        Assert.False(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void SaveAndContinueCommand_EnabledAfterSelection()
    {
        var vm = new WorkLocationViewModel();
        vm.SelectedLocation = vm.ApprovedLocations[0];
        Assert.True(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void WorkLocationOption_HasSubTitle()
    {
        var option = new WorkLocationOption("Office", "OFFICE", "Your registered office");
        Assert.Equal("Your registered office", option.SubTitle);
    }

    [Fact]
    public async Task SaveAndContinue_WithLiveFix_WritesGpsStringsToPrefs()
    {
        var prefs = new FakePreferencesStore();
        var loc   = new FixedLocationService(new GeoPoint(13.0827, 80.2707));
        var vm    = new WorkLocationViewModel(loc, prefs);

        await vm.DetectLiveLocationCommand.ExecuteAsync(null);
        vm.SelectedLocation = vm.ApprovedLocations[0];
        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal((13.0827).ToString("G17"), prefs.Get("onevo.live_latitude",  ""));
        Assert.Equal((80.2707).ToString("G17"), prefs.Get("onevo.live_longitude", ""));
    }

    [Fact]
    public async Task SaveAndContinue_SavesGenericOfficeCode()
    {
        var prefs = new FakePreferencesStore();
        var vm    = new WorkLocationViewModel(new FixedLocationService(null), prefs);
        vm.SelectedLocation = vm.ApprovedLocations[0]; // Office

        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal("OFFICE", prefs.Get("onevo.work_location_code", ""));
        Assert.Equal("Office", prefs.Get("onevo.work_location_display", ""));
    }

    [Fact]
    public async Task SaveAndContinue_SavesWfhCode()
    {
        var prefs = new FakePreferencesStore();
        var vm    = new WorkLocationViewModel(new FixedLocationService(null), prefs);
        vm.SelectedLocation = vm.ApprovedLocations[1]; // Work From Home

        await vm.SaveAndContinueCommand.ExecuteAsync(null);

        Assert.Equal("WFH", prefs.Get("onevo.work_location_code", ""));
        Assert.Equal("Work From Home", prefs.Get("onevo.work_location_display", ""));
    }
}
```

This removes the old 4-entry/city-specific tests (`ApprovedLocations_HasFourEntries`,
`ApprovedLocations_ContainsChennaiOffice`, `ApprovedLocations_ContainsBangaloreOffice`,
`ApprovedLocations_ContainsHyderabadOffice`, `FilteredLocations_FiltersOnSearchText`) and replaces
them with the two-option equivalents, plus two new save-path tests confirming the generic
`OFFICE`/`WFH` codes are what actually gets persisted.

- [ ] **Step 2: Commit the test changes**

```bash
cd C:/HR/tray_app_maui
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs
git commit -m "test: update WorkLocationViewModel tests for Office/WFH model"
```

---

### Task 2: Run the tests to confirm they fail against the old implementation

**Files:** none (verification step)

- [ ] **Step 1: Run the WorkLocationViewModel test filter**

Run:
```bash
cd C:/HR/tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WorkLocationViewModelTests"
```

Expected: Several FAIL — at minimum `ApprovedLocations_HasTwoEntries` (currently 4 entries),
`ApprovedLocations_ContainsOffice` (currently no entry with `Code == "OFFICE"`), and
`FindNearestOffice_NearChennai_SelectsOffice` (currently returns `Code == "CHENNAI"`, and
`NearestCity` doesn't exist yet on `NearestMatch` so this won't even compile until Task 3 is
partially done — a build error counts as "fails" for this step).

---

### Task 3: Implement the ViewModel changes

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`

- [ ] **Step 1: Replace `ApprovedLocations` and add the internal geofence table**

Replace lines 10-17 (the `ApprovedLocations` property):

```csharp
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        // Approx office pins for nearest-match against live GPS.
        new("Chennai Office",   "CHENNAI",   "Tamil Nadu, India",  13.0827, 80.2707),
        new("Bangalore Office", "BANGALORE", "Karnataka, India",  12.9716, 77.5946),
        new("Hyderabad Office", "HYDERABAD", "Telangana, India",  17.3850, 78.4867),
        new("Work From Home",   "WFH",       "Remote Location",   null,    null)
    ];
```

with:

```csharp
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        new("Office",         "OFFICE", "Your registered office"),
        new("Work From Home", "WFH",    "Remote Location")
    ];

    /// <summary>Approved office coordinates used only to decide Office vs. Work From Home —
    /// never surfaced as separate selectable options or sent to the backend.</summary>
    private static readonly (string City, double Lat, double Lon)[] OfficeGeofences =
    [
        ("Chennai",   13.0827, 80.2707),
        ("Bangalore", 12.9716, 77.5946),
        ("Hyderabad", 17.3850, 78.4867),
    ];
```

- [ ] **Step 2: Remove `SearchText` and `FilteredLocations`**

Delete this line (originally line 23):

```csharp
    [ObservableProperty] private string _searchText = string.Empty;
```

Delete these two members (originally lines 41-49):

```csharp
    public IEnumerable<WorkLocationOption> FilteredLocations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? ApprovedLocations
            : ApprovedLocations.Where(l =>
                l.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || l.SubTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(FilteredLocations));
```

- [ ] **Step 3: Update `DetectLiveLocationAsync` status messages**

Replace this block (originally lines 95-105):

```csharp
            var match = FindNearestOffice(point.Latitude, point.Longitude);
            if (match is null)
            {
                LiveLocationStatus = $"Live location: {LiveCoordsText}. No office nearby — pick a location.";
                return;
            }

            SelectedLocation = match.Option;
            LiveLocationStatus = match.IsRemoteFallback
                ? $"Live location: {LiveCoordsText}. Far from offices — suggested Work From Home."
                : $"Live location: {LiveCoordsText}. Nearest: {match.Option.DisplayName} ({match.DistanceKm:F1} km).";
```

with:

```csharp
            var match = FindNearestOffice(point.Latitude, point.Longitude);
            if (match is null)
            {
                LiveLocationStatus = $"Live location: {LiveCoordsText}. No office configured — pick a location.";
                return;
            }

            SelectedLocation = match.Option;
            LiveLocationStatus = match.IsRemoteFallback
                ? $"Live location: {LiveCoordsText}. Far from office — Work From Home selected."
                : $"Live location: {LiveCoordsText}. Near office ({match.NearestCity}, {match.DistanceKm:F1} km) — Office selected.";
```

- [ ] **Step 4: Rewrite `FindNearestOffice` and the `NearestMatch` record**

Replace `FindNearestOffice` (originally lines 117-147):

```csharp
    /// <summary>Picks nearest office with coordinates; falls back to WFH if all are far.</summary>
    public NearestMatch? FindNearestOffice(double lat, double lon)
    {
        WorkLocationOption? best = null;
        var bestKm = double.MaxValue;

        foreach (var loc in ApprovedLocations)
        {
            if (loc.Latitude is null || loc.Longitude is null)
                continue;

            var km = HaversineKm(lat, lon, loc.Latitude.Value, loc.Longitude.Value);
            if (km < bestKm)
            {
                bestKm = km;
                best = loc;
            }
        }

        if (best is null)
            return null;

        if (bestKm > NearestOfficeMaxKm)
        {
            var wfh = ApprovedLocations.FirstOrDefault(l => l.Code == "WFH");
            if (wfh is not null)
                return new NearestMatch(wfh, bestKm, IsRemoteFallback: true);
        }

        return new NearestMatch(best, bestKm, IsRemoteFallback: false);
    }
```

with:

```csharp
    /// <summary>Decides Office vs. Work From Home from the nearest approved geofence;
    /// the specific city is kept only for the live-status message, never persisted.</summary>
    public NearestMatch? FindNearestOffice(double lat, double lon)
    {
        if (OfficeGeofences.Length == 0)
            return null;

        string? nearestCity = null;
        var bestKm = double.MaxValue;

        foreach (var (city, officeLat, officeLon) in OfficeGeofences)
        {
            var km = HaversineKm(lat, lon, officeLat, officeLon);
            if (km < bestKm)
            {
                bestKm = km;
                nearestCity = city;
            }
        }

        var isRemote = bestKm > NearestOfficeMaxKm;
        var option = ApprovedLocations.First(l => l.Code == (isRemote ? "WFH" : "OFFICE"));
        return new NearestMatch(option, nearestCity, bestKm, isRemote);
    }
```

Replace the `NearestMatch` record (originally line 179):

```csharp
    public sealed record NearestMatch(WorkLocationOption Option, double DistanceKm, bool IsRemoteFallback);
```

with:

```csharp
    public sealed record NearestMatch(WorkLocationOption Option, string? NearestCity, double DistanceKm, bool IsRemoteFallback);
```

- [ ] **Step 5: Build the TrayApp project**

Run:
```bash
cd C:/HR/tray_app_maui
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj
```

Expected: `Build succeeded.` — this only compiles the app project; the test project is checked in
the next task.

---

### Task 4: Run the tests to confirm they pass

**Files:** none (verification step)

- [ ] **Step 1: Run the WorkLocationViewModel test filter**

Run:
```bash
cd C:/HR/tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WorkLocationViewModelTests"
```

Expected: All tests PASS (16 tests: the 14 rewritten in Task 1 plus no leftover old ones).

- [ ] **Step 2: Run the full TrayApp test suite to check for unrelated breakage**

Run:
```bash
cd C:/HR/tray_app_maui
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
```

Expected: All tests PASS. If anything outside `WorkLocationViewModelTests` fails, stop and
investigate before continuing — it means something else in the test project referenced the old
4-option shape (e.g. a shared fixture) that this plan didn't anticipate.

- [ ] **Step 3: Commit the ViewModel changes**

```bash
cd C:/HR/tray_app_maui
git add ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
git commit -m "feat: simplify work location picker to Office/WFH"
```

---

### Task 5: Update the XAML to match the two-option model

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml`

- [ ] **Step 1: Drop the search-box row from `RightPane`'s row definitions**

Replace (originally line 53):

```xml
        <Grid x:Name="RightPane" Grid.Column="1" RowDefinitions="Auto,Auto,Auto,Auto,*,Auto" RowSpacing="0">
```

with:

```xml
        <Grid x:Name="RightPane" Grid.Column="1" RowDefinitions="Auto,Auto,Auto,*,Auto" RowSpacing="0">
```

- [ ] **Step 2: Delete the search box `Border`**

Delete this entire block (originally lines 124-135, the row between the "Find a Location" banner
and the `CollectionView`):

```xml
        <Border Grid.Row="3" Style="{StaticResource GlassInputRow}" Margin="0,0,0,12">
          <HorizontalStackLayout Spacing="10">
            <Label Text="{StaticResource IconSearch}" FontFamily="Segoe MDL2 Assets"
                   FontSize="16" TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
            <Entry Placeholder="Search location..."
                   Text="{Binding SearchText}"
                   BackgroundColor="Transparent"
                   PlaceholderColor="{StaticResource TextMuted}"
                   FontSize="14"
                   HorizontalOptions="Fill" />
          </HorizontalStackLayout>
        </Border>
```

- [ ] **Step 3: Rebind the `CollectionView` and shift it up a row**

Replace (originally line 137-138):

```xml
        <CollectionView Grid.Row="4"
                        ItemsSource="{Binding FilteredLocations}"
```

with:

```xml
        <CollectionView Grid.Row="3"
                        ItemsSource="{Binding ApprovedLocations}"
```

- [ ] **Step 4: Shift the confirm button up a row**

Replace (originally line 193):

```xml
        <Border Grid.Row="5" Style="{StaticResource GradientButtonBorder}">
```

with:

```xml
        <Border Grid.Row="4" Style="{StaticResource GradientButtonBorder}">
```

- [ ] **Step 5: Build the TrayApp project**

Run:
```bash
cd C:/HR/tray_app_maui
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj
```

Expected: `Build succeeded.` — this catches any XAML binding typos (MAUI XAML compilation is
part of the build).

- [ ] **Step 6: Commit the XAML changes**

```bash
cd C:/HR/tray_app_maui
git add ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml
git commit -m "feat: remove search box, show Office/WFH cards on location page"
```

---

### Task 6: Manual smoke test in the running TrayApp

**Files:** none (manual verification step)

- [ ] **Step 1: Stop the currently running dev instances**

```bash
powershell -Command "Get-Process | Where-Object { \$_.ProcessName -like '*ONEVO.Agent*' } | Stop-Process -Force"
```

- [ ] **Step 2: Rebuild and relaunch the TrayApp (Service does not need changes for this task)**

```bash
cd C:/HR/tray_app_maui
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj
powershell -Command "$env:DOTNET_ENVIRONMENT='Development'; Start-Process -FilePath 'C:\HR\tray_app_maui\ONEVO.Agent.Service\bin\Debug\net10.0-windows\ONEVO.Agent.Service.exe' -WorkingDirectory 'C:\HR\tray_app_maui\ONEVO.Agent.Service\bin\Debug\net10.0-windows'"
powershell -Command "$env:DOTNET_ENVIRONMENT='Development'; Start-Process -FilePath 'C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ONEVO.Agent.TrayApp.exe' -WorkingDirectory 'C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64'"
```

The Agent Service was already re-enrolled from the previous activation test in this session (it
holds a valid device token from the `QHXLQQSX` exchange), so the TrayApp should skip straight past
`ConnectWorkspacePage`/`PrepareWorkspacePage` to the Work Location screen — no new activation code
needed for this smoke test.

- [ ] **Step 3: Verify the two-card layout and auto-select**

Navigate to the Work Location screen (`//location`) and confirm:
- Exactly two cards are visible: "Office" and "Work From Home" (no search box, no per-city cards).
- One of the two is pre-selected (checkmark) based on live GPS, matching the machine's current
  location relative to the Chennai/Bangalore/Hyderabad geofences.
- Tapping the other card switches the selection (manual override still works).
- "Confirm Location" is enabled once a card is selected.

---

### Task 7: Update the design spec status

**Files:**
- Modify: `docs/superpowers/specs/2026-08-16-work-location-office-wfh-design.md`

- [ ] **Step 1: Mark the spec as implemented**

In `docs/superpowers/specs/2026-08-16-work-location-office-wfh-design.md`, change:

```
**Status:** Approved
```

to:

```
**Status:** Implemented
```

- [ ] **Step 2: Commit**

```bash
cd C:/HR/tray_app_maui
git add docs/superpowers/specs/2026-08-16-work-location-office-wfh-design.md
git commit -m "docs: mark work-location Office/WFH spec as implemented"
```
