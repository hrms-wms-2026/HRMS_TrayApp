# Check-In Idempotency Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When `ActivitySyncService.FlushFacePhotoAsync` retries a check-in submission after a `5xx`/`429` response (the buffered record stays queued and is retried next cycle), send the same `Idempotency-Key` header on every attempt so the backend collapses retries into a single `EmployeeCheckIn` row instead of creating duplicates.

**Architecture:** `BufferedCollectionRecord.Record.EventId` is already a stable per-record GUID (`Guid.NewGuid().ToString("N")`, assigned once when the record is captured and unchanged across retries, since the same buffered row is re-read from the buffer each flush cycle). It is the natural idempotency key: same buffered record → same `EventId` → same header value on every retry, until the record is acknowledged or quarantined and removed from the buffer. This plan adds that header to the existing check-in POST request; the companion backend plan (`HRMS-Backend-v1/docs/superpowers/plans/2026-08-19-checkin-idempotency-key.md`) makes the backend endpoint honor it.

**Tech Stack:** .NET, xUnit, the existing `CapturingHttpClientFactory` test double in `ActivitySyncServiceTests.cs`.

---

### Task 1: Send `Idempotency-Key` header on check-in submit

**Files:**
- Modify: `ONEVO.Agent.Service/Sync/ActivitySyncService.cs:494-507`
- Test: `tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `ActivitySyncServiceTests.cs`, directly after `FlushAsync_FacePhotoRecord_PostsCheckInThenFaceScan` (after line 492):

```csharp
[Fact]
public async Task FlushAsync_FacePhotoRecord_CheckInRequest_SendsIdempotencyKeyHeader()
{
    var payload = new FacePhotoPayload
    {
        Format = "jpeg",
        Data   = Convert.ToBase64String(new byte[] { 1, 2, 3 })
    };
    var eventId = Guid.NewGuid().ToString("N");
    var record = new CollectionRecord
    {
        EventId          = eventId,
        RecordType       = CollectionRecordTypes.FacePhoto,
        SchemaVersion    = CollectionSchemaVersions.FacePhotoV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId         = "test",
        Payload          = JsonSerializer.SerializeToElement(payload)
    };
    var buffer = ActivityRecordBuffer.CreateInMemory();
    buffer.TryEnqueue(record);

    string? capturedIdempotencyKey = null;
    var factory = new CapturingHttpClientFactory(req =>
    {
        if (req.RequestUri!.AbsolutePath.EndsWith("/check-in", StringComparison.Ordinal)
            && req.Method == HttpMethod.Post)
        {
            capturedIdempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.FirstOrDefault()
                : null;
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
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    WithJwt(credentials =>
    {
        var svc = Build(buffer, factory, credentials: credentials);
        svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    });

    Assert.Equal(eventId, capturedIdempotencyKey);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~FlushAsync_FacePhotoRecord_CheckInRequest_SendsIdempotencyKeyHeader"`
Expected: FAIL — `capturedIdempotencyKey` is `null` because no header is sent yet.

- [ ] **Step 3: Add the header in the check-in request**

In `ONEVO.Agent.Service/Sync/ActivitySyncService.cs`, inside `FlushFacePhotoAsync`, find the check-in request block (around line 495-507):

```csharp
        using (var req = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.CheckInSubmit)
        {
            Content = JsonContent.Create(new CheckInSubmitRequest
            {
                Latitude         = photo.Latitude,
                Longitude        = photo.Longitude,
                LocationAccuracy = photo.LocationAccuracy,
                LocationAddress  = photo.LocationAddress
            })
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
```

Add the idempotency header right after the `Authorization` line:

```csharp
        using (var req = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.CheckInSubmit)
        {
            Content = JsonContent.Create(new CheckInSubmitRequest
            {
                Latitude         = photo.Latitude,
                Longitude        = photo.Longitude,
                LocationAccuracy = photo.LocationAccuracy,
                LocationAddress  = photo.LocationAddress
            })
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            req.Headers.Add("Idempotency-Key", buffered.Record.EventId);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~FlushAsync_FacePhotoRecord_CheckInRequest_SendsIdempotencyKeyHeader"`
Expected: PASS

- [ ] **Step 5: Run the full ActivitySyncService test suite to confirm no regressions**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~ActivitySyncServiceTests"`
Expected: PASS (including `FlushAsync_FacePhotoRecord_CheckInFails5xx_RequeuesRecord` — the record keeps the same `EventId` across the requeue, so the retry will now carry the same key)

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.Service/Sync/ActivitySyncService.cs tests/ONEVO.Agent.Service.Tests/Sync/ActivitySyncServiceTests.cs
git commit -m "feat: send Idempotency-Key header on check-in submit retries"
```

---

## Self-Review

**Spec coverage:** Duplicate check-in rows on TrayApp retry → fixed by sending a stable per-record `Idempotency-Key` (the buffered record's existing `EventId`) on every check-in submit attempt, including retries.

**Placeholder scan:** No TBD/TODO; the code block is copy-pasteable as a direct insertion.

**Type consistency:** `buffered.Record.EventId` is a `string` (per `CollectionRecord.EventId`), which matches `HttpRequestHeaders.Add(string, string)` — no cast needed. `CollectionRecordTypes.FacePhoto` / `CollectionSchemaVersions.FacePhotoV1` are the same constants already used by `MakeRecord` in this test file.

**Scope note — do not also fix face-scan upload retries:** the face-scan upload step (Step 2 in `FlushFacePhotoAsync`) already avoids duplicates by design — its own code comment says "check-in already exists — don't retry on failure to avoid duplicates," and any failure there quarantines the record rather than requeuing it. No `Idempotency-Key` is needed on that request; adding one would be dead code since it never retries.

**Dependency on the backend plan:** this header is inert until the backend endpoint honors it. Apply `HRMS-Backend-v1/docs/superpowers/plans/2026-08-19-checkin-idempotency-key.md` Task 1 either before or in the same release as this plan — order between the two doesn't matter since older TrayApp builds without the header still work unchanged (the backend's `[Idempotent]` filter is a no-op when the header is absent).
