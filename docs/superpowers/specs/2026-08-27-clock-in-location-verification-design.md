# Clock-In Location Verification Design

**Date:** 2026-08-27

## Goal

Capture and confirm an employee's work-location reference during onboarding, take a fresh GPS fix on every Clock In attempt, and warn when the new fix does not match the confirmed work location.

## Approved Product Behaviour

1. The `Setting up your workspace` screen shows a `Work Location` action card above `Profile Picture`.
2. Tapping the card opens a dedicated `Confirm Today's Work Location` screen.
3. The employee chooses exactly one of:
   - `Office`
   - `Work From Home`
   - `Other Approved Location`
4. The app requests Windows location permission and captures latitude, longitude, accuracy, and UTC capture time.
5. Confirming the page saves that fix as the employee's work-location reference for this activation.
6. The setup page shows `Location verified — <selection>` when the employee returns.
7. `Continue` is enabled only after workspace preparation, location confirmation, and face capture are complete.
8. Every Clock In click captures a new GPS fix; a cached onboarding fix is never reused as the current Clock In fix.
9. The current fix is compared with the saved reference using a Haversine distance calculation.
10. A match proceeds with the existing face-verification/Clock In flow without another prompt.
11. A mismatch, inaccurate fix, permission denial, disabled location service, or timeout shows an in-app warning with:
    - `Retry Location`
    - `Clock In Anyway`
    - `Cancel`
12. `Clock In Anyway` is allowed because Windows location can be unavailable or imprecise. The final enforcement decision remains a server/admin policy concern.
13. A warning also produces a Windows toast so the location exception is visible even if the app loses focus.
14. A Clock In that continues with a location result queues a durable `clock_in_location_verification` collection record. The existing offline buffer retries it until the server acknowledges it.
15. A `Mismatch` record is the backend contract for creating the admin notification/audit item. `Unavailable` and `Inaccurate` are also auditable but must be displayed separately from a confirmed mismatch.

## Geofence Rules

- Default reference radius:
  - Office: `300 metres`
  - Work From Home: `250 metres`
  - Other Approved Location: `250 metres`
- Maximum acceptable current-fix accuracy: `100 metres`.
- Effective match radius is `max(reference radius, reference accuracy + current accuracy)` so normal GPS drift does not create false mismatch alerts.
- If current accuracy is greater than `100 metres`, verdict is `Inaccurate`; do not label it as `Mismatch`.
- If no fix is available, verdict is `Unavailable`; do not fabricate coordinates or fall back to IP-based location.
- If a valid fix is outside the effective radius, verdict is `Mismatch`.
- If it is inside the effective radius, verdict is `Match`.

## Clock-In Decision Flow

```text
Clock In clicked
  -> fresh GPS capture
     -> no saved reference: return to Setup; do not clock in
     -> GPS unavailable/inaccurate: warning + Retry / Clock In Anyway / Cancel
     -> GPS valid: compare with saved reference
        -> Match: continue to face verification when required, then Clock In
        -> Mismatch: warning + Retry / Clock In Anyway / Cancel
           -> Retry: capture a new GPS fix
           -> Cancel: remain Ready
           -> Clock In Anyway: continue and queue an auditable exception record
```

## Notification Copy

- Mismatch title: `Work location changed`
- Mismatch body: `You are {distance} m away from {location}. Retry location or clock in anyway.`
- Inaccurate title: `Location accuracy is low`
- Inaccurate body: `Current GPS accuracy is ±{accuracy} m. Move near a window and retry.`
- Unavailable title: `Location could not be verified`
- Unavailable body: `Turn on Windows Location Services and retry, or clock in anyway.`

## Data Model

`WorkLocationReference` is activation-scoped and contains:

- location kind/code and display name
- reference latitude and longitude
- reference accuracy
- allowed radius in metres
- confirmed-at UTC timestamp

`ClockInLocationVerification` is attempt-scoped and contains:

- unique attempt ID
- current GPS fix, when available
- reference snapshot
- verdict: `Match`, `Mismatch`, `Unavailable`, or `Inaccurate`
- calculated distance and effective radius, when calculable
- machine-readable failure reason, when no valid comparison exists

The reference and current fix are deliberately separate. A new Clock In fix must never overwrite the approved reference.

## Persistence and Privacy

- Location is read only during setup confirmation and Clock In attempts.
- There is no background or continuous location polling.
- No reverse-geocoded street address is required.
- The current Clock In fix is kept in an in-memory context until the lifecycle command is completed.
- The confirmed reference is activation-scoped and is removed on successful Sign Out.
- Device installation identity and completed work-session history are not removed by this feature.

## Backend Contract

The service queues a collection record with:

- `record_type`: `clock_in_location_verification`
- `schema_version`: `1.0`
- `event_id`: location-attempt ID
- payload: current fix, reference snapshot, verdict, distance, effective radius, and reason

The collection endpoint already provides durable, idempotent delivery. Backend processing must create the admin notification/audit entry when `verdict == Mismatch`; the tray remains responsible for the employee warning.

## Error and Recovery Behaviour

- Permission denied: show the unavailable warning; do not repeatedly prompt in a loop.
- Location services disabled: same warning, with a retry after the employee enables Windows Location Services.
- Timeout: same warning; Retry performs a completely new request.
- Poor accuracy: show `Inaccurate`, not `Mismatch`.
- No server connection: local warning and Clock In decision still work; the service's durable buffer holds the record for later sync.
- Camera verification enabled: preserve the location attempt in memory across the face page and attach it to the eventual Clock In lifecycle command.
- Clock In failure: clear the in-memory attempt so the next click obtains a fresh GPS fix.

## Acceptance Criteria

- Setup cannot complete without a confirmed live work-location reference and completed face capture.
- Every Clock In attempt asks the location service for a fresh fix.
- A valid in-radius fix does not show a warning.
- An out-of-radius fix shows the exact mismatch warning and does not send Clock In until the employee chooses `Clock In Anyway`.
- Retry replaces the pending attempt with a newly captured fix.
- Camera and non-camera Clock In paths send the same location-verification payload.
- Successful Sign Out removes the reference.
- Unit tests cover match, mismatch, drift tolerance, inaccurate, unavailable, retry, cancel, anyway, camera hand-off, IPC serialization, and durable record creation.

## Out of Scope

- Continuous employee tracking.
- Reverse geocoding or displaying a home street address.
- Silently blocking attendance solely because GPS is unavailable.
- Building the backend admin dashboard UI inside the tray-app repository.
