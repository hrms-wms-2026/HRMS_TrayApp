# Work Location: Simplify to Office / Work From Home

**Date:** 2026-08-16
**Status:** Approved
**Scope:** `ONEVO.Agent.TrayApp` — `ViewModels/WorkLocationViewModel.cs`, `Views/WorkLocationPage.xaml`. No backend or Service changes.

## Problem

`WorkLocationPage` currently shows one selectable card per approved office (Chennai, Bangalore,
Hyderabad) plus Work From Home, with a search box to filter them. The user wants a simpler,
generic two-choice screen — **Office** and **Work From Home** — with live GPS still deciding which
one is pre-selected. The specific office identity (which city) is not needed downstream; only a
binary Office/WFH signal is persisted.

## Decisions

1. **No per-office identity downstream.** Selecting "Office" saves `Code = "OFFICE"` regardless of
   which city matched. The three office coordinates remain internal, used only to decide Office vs.
   WFH — they are never surfaced as separate selectable rows or sent to the backend as a city code.
2. **Fallback rule unchanged.** If live location is not within `NearestOfficeMaxKm` (80 km) of any
   approved office geofence, auto-select falls back to Work From Home — same threshold and
   direction as today's per-city logic, just collapsed to two outcomes instead of four.

## Design

### Displayed options (`ApprovedLocations`, bound to the `CollectionView`)

Exactly two `WorkLocationOption` entries, no lat/lon on the option itself (matching moves to a
separate internal table — see below):

```csharp
new("Office",          "OFFICE", "Your registered office"),
new("Work From Home",  "WFH",    "Remote Location")
```

The existing card template, selection checkmark, and `IsSelected` binding are unchanged — they
already work generically off `WorkLocationOption`, independent of how many entries exist.

### Internal geofence table (not displayed)

A private `static readonly` list replaces the lat/lon fields that used to live on the Chennai/
Bangalore/Hyderabad `WorkLocationOption` entries:

```csharp
private static readonly (string City, double Lat, double Lon)[] OfficeGeofences =
[
    ("Chennai",   13.0827, 80.2707),
    ("Bangalore", 12.9716, 77.5946),
    ("Hyderabad", 17.3850, 78.4867),
];
```

### Matching logic (`FindNearestOffice`)

Same Haversine distance calculation as today, run against `OfficeGeofences` instead of
`ApprovedLocations`. Returns which display option to select (`Office` or `WorkFromHome`) plus the
nearest city name and distance — kept only for the status-line message, never persisted or sent to
the backend:

```csharp
public sealed record NearestMatch(WorkLocationOption Option, string? NearestCity, double DistanceKm, bool IsRemoteFallback);
```

- Nearest geofence ≤ 80 km → match the `Office` option; `IsRemoteFallback = false`.
- Nearest geofence > 80 km (or no geofences) → match the `WorkFromHome` option; `IsRemoteFallback = true`.

### Live detection flow (`DetectLiveLocationAsync`)

Unchanged shape — gets a GPS fix, runs `FindNearestOffice`, sets `SelectedLocation`. Status text
becomes generic instead of naming a city:

- Office match: `"Live location: {coords}. Near office ({city}, {distance} km) — Office selected."`
- WFH fallback: `"Live location: {coords}. Far from office — Work From Home selected."`
- No fix: unchanged ("Could not get live location…").

### Search box removal

`SearchText`, `FilteredLocations`, and the search `Entry` in `WorkLocationPage.xaml` are removed —
with two fixed options, filtering adds no value. `CollectionView.ItemsSource` binds directly to
`ApprovedLocations`.

### Save behavior (`SaveAndContinueCommand`)

Unchanged pattern, narrower value set: `onevo.work_location_code` is now only ever `"OFFICE"` or
`"WFH"` (previously could be `"CHENNAI"`/`"BANGALORE"`/`"HYDERABAD"`/`"WFH"`).

## Out of scope

- Backend/API changes — the backend already only needs to know whether the value is not empty;
  no schema change.
- Adding/removing which cities count as office geofences — still Chennai/Bangalore/Hyderabad,
  hardcoded, same as today.
- Manual override UX — unchanged: user can still tap either card regardless of what auto-detect
  picked.

## Testing

Existing unit tests reference `ApprovedLocations` count and city-specific codes/display names
(`WorkLocationViewModelTests` or similar) — these will need updating for the new 2-item list and
`OFFICE`/`WFH` codes. Haversine/distance-threshold behavior itself is unchanged and should keep
passing once tests target `OfficeGeofences` instead of the old office `WorkLocationOption` entries.
