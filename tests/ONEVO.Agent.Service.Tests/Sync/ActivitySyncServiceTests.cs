using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

#pragma warning disable CA1001 // RecordingHttpClientFactory creates HttpClient (test code, no disposal needed)

namespace ONEVO.Agent.Service.Tests.Sync;

[Collection(CredentialStoreFileCollection.Name)]
public class ActivitySyncServiceTests
{
    private static ActivitySyncService Build(
        ActivityRecordBuffer buffer,
        IHttpClientFactory? factory = null,
        AgentOptions? options = null,
        IEvidenceProtector? protector = null,
        EvidenceSpoolStore? spoolStore = null,
        CredentialStore? credentials = null)
    {
        return new ActivitySyncService(
            NullLogger<ActivitySyncService>.Instance,
            buffer,
            credentials ?? new CredentialStore(),
            factory ?? new NeverCalledHttpClientFactory(),
            protector ?? new PassthroughEvidenceProtector(),
            spoolStore ?? new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
            Options.Create(options ?? new AgentOptions { ApiBaseUrl = "https://api.example.com" }));
    }

    private static CollectionRecord MakeRecord(string type, string schema, object payload) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = type,
        SchemaVersion = schema,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "test",
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    private static InactivityCaptureAttemptPayload MakeAttempt(
        Guid id,
        string outcome,
        string? sha256 = null) => new()
    {
        AttemptId = id,
        PolicyVersion = "policy-v3",
        IdleStartedAt = DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
        PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z"),
        DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z"),
        CapturedAt = outcome == InactivityCaptureOutcomes.Captured
            ? DateTimeOffset.Parse("2026-08-10T01:05:04Z")
            : null,
        IdleDurationSeconds = 300,
        MonitorCount = outcome == InactivityCaptureOutcomes.Captured ? 2 : 0,
        Outcome = outcome,
        ContentType = outcome == InactivityCaptureOutcomes.Captured ? "image/jpeg" : null,
        Sha256 = sha256
    };

    private static void WithJwt(Action<CredentialStore> action)
    {
        var credentials = new CredentialStore();
        try
        {
            credentials.StoreDeviceJwt("test-jwt");
            action(credentials);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task FlushAsync_EmptyBuffer_DoesNothing()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var svc = Build(buffer);

        await svc.FlushAsync(CancellationToken.None);

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_NoJwt_RequeusBatch()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.AppUsageSnapshot,
            CollectionSchemaVersions.AppUsageSnapshotV1,
            new AppUsageSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, ProcessName = "code.exe" }));

        var svc = Build(buffer);

        await svc.FlushAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_NoApiBaseUrl_RequeusBatch()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.DeviceStateSnapshot,
            CollectionSchemaVersions.DeviceStateSnapshotV1,
            new DeviceStateSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, IdleSeconds = 0, IsIdle = false }));

        var svc = Build(buffer, options: new AgentOptions { ApiBaseUrl = string.Empty });

        await svc.FlushAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_EmptyBuffer_HttpClientNeverCalled()
    {
        var factory = new RecordingHttpClientFactory(HttpStatusCode.OK);
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var svc = Build(buffer, factory);

        await svc.FlushAsync(CancellationToken.None);

        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void ActivityRecordBuffer_AcceptsAllThreeRecordTypes()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();

        var activity = MakeRecord(
            CollectionRecordTypes.ActivitySnapshot,
            CollectionSchemaVersions.ActivitySnapshotV1,
            new ActivitySnapshotPayload
            {
                CapturedAt = DateTimeOffset.UtcNow,
                KeyboardEventsCount = 5,
                MouseEventsCount = 3,
                ActiveSeconds = 60,
                IdleSeconds = 0,
                IntensityScore = 10m
            });

        var appUsage = MakeRecord(
            CollectionRecordTypes.AppUsageSnapshot,
            CollectionSchemaVersions.AppUsageSnapshotV1,
            new AppUsageSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, ProcessName = "zoom.exe" });

        var deviceState = MakeRecord(
            CollectionRecordTypes.DeviceStateSnapshot,
            CollectionSchemaVersions.DeviceStateSnapshotV1,
            new DeviceStateSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, IdleSeconds = 30, IsIdle = false });

        Assert.True(buffer.TryEnqueue(activity));
        Assert.True(buffer.TryEnqueue(appUsage));
        Assert.True(buffer.TryEnqueue(deviceState));
        Assert.Equal(3, buffer.Count);

        var batch = buffer.PeekPendingBatch(10);
        Assert.Equal(3, batch.Count);
        Assert.Contains(batch, r => r.Record.RecordType == CollectionRecordTypes.ActivitySnapshot);
        Assert.Contains(batch, r => r.Record.RecordType == CollectionRecordTypes.AppUsageSnapshot);
        Assert.Contains(batch, r => r.Record.RecordType == CollectionRecordTypes.DeviceStateSnapshot);
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_DeclinedInactivityAttempt_UsesExactFormFieldNamesWithoutFile()
    {
        var attemptId = Guid.NewGuid();
        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Declined),
            "test",
            encryptedSpoolPath: null,
            encryptedSize: 0,
            expiresAt: DateTimeOffset.UtcNow.AddHours(72)));

        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        var request = Assert.Single(factory.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(AgentApiRoutes.InactivityAttemptSubmit, request.Uri!.AbsolutePath, StringComparison.Ordinal);

        var fields = request.FormFields;
        Assert.Equal(attemptId.ToString("N"), fields[InactivityAttemptFormFields.AttemptId]);
        Assert.Equal("policy-v3", fields[InactivityAttemptFormFields.PolicyVersion]);
        Assert.Equal("2026-08-10T01:00:00.0000000+00:00", fields[InactivityAttemptFormFields.IdleStartedAt]);
        Assert.Equal("2026-08-10T01:05:00.0000000+00:00", fields[InactivityAttemptFormFields.PromptedAt]);
        Assert.Equal("2026-08-10T01:05:03.0000000+00:00", fields[InactivityAttemptFormFields.DecisionAt]);
        Assert.Equal("300", fields[InactivityAttemptFormFields.IdleDurationSeconds]);
        Assert.Equal("0", fields[InactivityAttemptFormFields.MonitorCount]);
        Assert.Equal(InactivityCaptureOutcomes.Declined, fields[InactivityAttemptFormFields.Outcome]);
        Assert.False(fields.ContainsKey(InactivityAttemptFormFields.File));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_CapturedInactivityAttempt_DecryptsAndAttachesJpeg()
    {
        var attemptId = Guid.NewGuid();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var sha256 = Convert.ToHexString(SHA256.HashData(jpegBytes)).ToLowerInvariant();
        var spoolDir = Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}");
        var spoolStore = new EvidenceSpoolStore(spoolDir);
        var protector = new PassthroughEvidenceProtector();
        var spoolPath = spoolStore.Write(attemptId, protector.Protect(jpegBytes, attemptId));

        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured, sha256),
            "test",
            spoolPath,
            jpegBytes.Length,
            DateTimeOffset.UtcNow.AddHours(72)));

        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, protector: protector, spoolStore: spoolStore, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        var request = Assert.Single(factory.Requests);
        var fields = request.FormFields;
        Assert.Equal(InactivityCaptureOutcomes.Captured, fields[InactivityAttemptFormFields.Outcome]);
        Assert.Equal("image/jpeg", fields[InactivityAttemptFormFields.ContentType]);
        Assert.Equal(sha256, fields[InactivityAttemptFormFields.Sha256]);
        Assert.Equal(jpegBytes, fields[InactivityAttemptFormFields.File]);
        Assert.Equal(0, buffer.Count);
        Assert.False(File.Exists(spoolPath));
    }

    [Fact]
    public async Task FlushAsync_InactivityConflictAlreadyRecorded_AcknowledgesAndDeletesSpool()
    {
        var attemptId = Guid.NewGuid();
        var jpegBytes = new byte[] { 1, 2, 3, 4 };
        var spoolDir = Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}");
        var spoolStore = new EvidenceSpoolStore(spoolDir);
        var protector = new PassthroughEvidenceProtector();
        var spoolPath = spoolStore.Write(attemptId, protector.Protect(jpegBytes, attemptId));

        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            "test",
            spoolPath,
            jpegBytes.Length,
            DateTimeOffset.UtcNow.AddHours(72)));

        var factory = new CapturingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"code":"attempt_already_recorded"}""")
            });

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, protector: protector, spoolStore: spoolStore, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(0, buffer.Count);
        Assert.False(File.Exists(spoolPath));
    }

    [Fact]
    public async Task FlushAsync_InactivityServerError_LeavesPendingAndRetainsSpool()
    {
        var attemptId = Guid.NewGuid();
        var jpegBytes = new byte[] { 9, 8, 7 };
        var spoolDir = Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}");
        var spoolStore = new EvidenceSpoolStore(spoolDir);
        var protector = new PassthroughEvidenceProtector();
        var spoolPath = spoolStore.Write(attemptId, protector.Protect(jpegBytes, attemptId));

        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            "test",
            spoolPath,
            jpegBytes.Length,
            DateTimeOffset.UtcNow.AddHours(72)));

        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, protector: protector, spoolStore: spoolStore, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(1, buffer.Count);
        Assert.True(File.Exists(spoolPath));
    }

    [Fact]
    public async Task FlushAsync_FailedInactivityAttempt_BlocksLaterWorkSession()
    {
        var attemptId = Guid.NewGuid();
        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Declined),
            "test",
            null,
            0,
            DateTimeOffset.UtcNow.AddHours(72)));
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.WorkSession,
            CollectionSchemaVersions.WorkSessionV1,
            new WorkSessionPayload
            {
                SessionId = Guid.NewGuid(),
                ClockInAt = DateTimeOffset.UtcNow.AddHours(-8),
                ClockOutAt = DateTimeOffset.UtcNow,
                AccumulatedBreak = TimeSpan.FromMinutes(30),
                AccumulatedWork = TimeSpan.FromHours(7),
                BreakSessionCount = 1,
                ScheduleDisplay = "09:00 AM – 06:00 PM"
            }));

        var factory = new CapturingHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(AgentApiRoutes.InactivityAttemptSubmit, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(2, buffer.Count);
        Assert.Single(factory.Requests);
        Assert.EndsWith(AgentApiRoutes.InactivityAttemptSubmit, factory.Requests[0].Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlushAsync_ProcessesRecordsInRowOrder_NotGroupedByType()
    {
        var attemptId = Guid.NewGuid();
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.ActivitySnapshot,
            CollectionSchemaVersions.ActivitySnapshotV1,
            new ActivitySnapshotPayload
            {
                CapturedAt = DateTimeOffset.UtcNow,
                KeyboardEventsCount = 1,
                MouseEventsCount = 1,
                ActiveSeconds = 10,
                IdleSeconds = 0,
                IntensityScore = 1m
            }));
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Declined),
            "test",
            null,
            0,
            DateTimeOffset.UtcNow.AddHours(72)));
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.WorkSession,
            CollectionSchemaVersions.WorkSessionV1,
            new WorkSessionPayload
            {
                SessionId = Guid.NewGuid(),
                ClockInAt = DateTimeOffset.UtcNow.AddHours(-8),
                ClockOutAt = DateTimeOffset.UtcNow,
                AccumulatedBreak = TimeSpan.Zero,
                AccumulatedWork = TimeSpan.FromHours(8),
                BreakSessionCount = 0,
                ScheduleDisplay = "09:00 AM – 06:00 PM"
            }));

        var routes = new List<string>();
        var factory = new CapturingHttpClientFactory(request =>
        {
            routes.Add(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(3, routes.Count);
        Assert.EndsWith(AgentApiRoutes.ActivitySnapshots, routes[0], StringComparison.Ordinal);
        Assert.EndsWith(AgentApiRoutes.InactivityAttemptSubmit, routes[1], StringComparison.Ordinal);
        Assert.EndsWith(AgentApiRoutes.WorkSessionSubmit, routes[2], StringComparison.Ordinal);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task FlushAsync_MeetingSignalRecord_PostsToMeetingSignalsRoute()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.MeetingSignal,
            CollectionSchemaVersions.MeetingSignalV1,
            new MeetingSignalPayload
            {
                CapturedAt = DateTimeOffset.UtcNow,
                IsMeetingAppRunning = true,
                ProcessName = "teams.exe"
            }));

        var factory = new CapturingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.Accepted));

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        var request = Assert.Single(factory.Requests);
        Assert.EndsWith(AgentApiRoutes.MeetingSignals, request.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(0, buffer.Count);
    }

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

        Assert.Equal(new[] { "checkin", "facescan" }, callOrder);
        Assert.Equal(0, buffer.Count);
    }

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
    public async Task FlushAsync_InactivityAttempt404_QuarantinesAndDoesNotBlockLaterWorkSession()
    {
        // A hardcoded route returning 404 is a permanent condition (endpoint missing), not a
        // transient one — unlike the 5xx case in FlushAsync_FailedInactivityAttempt_BlocksLaterWorkSession
        // above, it must not deadlock everything queued behind it.
        var attemptId = Guid.NewGuid();
        var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Declined),
            "test",
            null,
            0,
            DateTimeOffset.UtcNow.AddHours(72)));
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.WorkSession,
            CollectionSchemaVersions.WorkSessionV1,
            new WorkSessionPayload
            {
                SessionId = Guid.NewGuid(),
                ClockInAt = DateTimeOffset.UtcNow.AddHours(-8),
                ClockOutAt = DateTimeOffset.UtcNow,
                AccumulatedBreak = TimeSpan.FromMinutes(30),
                AccumulatedWork = TimeSpan.FromHours(7),
                BreakSessionCount = 1,
                ScheduleDisplay = "09:00 AM – 06:00 PM"
            }));

        var factory = new CapturingHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(AgentApiRoutes.InactivityAttemptSubmit, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(0, buffer.Count);
        Assert.Equal(2, factory.Requests.Count);
    }

    [Fact]
    public async Task FlushAsync_AppUsageSnapshotBatch400_DropsAndDoesNotBlockLaterActivitySnapshot()
    {
        // A 400 (e.g. the backend's 24h freshness window rejecting a stale batch) will never
        // succeed on retry — it must be dropped, not requeued forever, so unrelated fresher data
        // queued behind it still gets a chance to sync in the same flush cycle.
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.AppUsageSnapshot,
            CollectionSchemaVersions.AppUsageSnapshotV1,
            new AppUsageSnapshotPayload
            {
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-3),
                ProcessName = "stale.exe"
            }));
        buffer.TryEnqueue(MakeRecord(
            CollectionRecordTypes.ActivitySnapshot,
            CollectionSchemaVersions.ActivitySnapshotV1,
            new ActivitySnapshotPayload
            {
                CapturedAt = DateTimeOffset.UtcNow,
                KeyboardEventsCount = 1,
                MouseEventsCount = 1,
                ActiveSeconds = 1,
                IdleSeconds = 0,
                IntensityScore = 1,
                ForegroundProcessName = "code.exe"
            }));

        var factory = new CapturingHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(AgentApiRoutes.AppUsageSnapshots, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        WithJwt(credentials =>
        {
            var svc = Build(buffer, factory, credentials: credentials);
            svc.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.Equal(0, buffer.Count);
        Assert.Equal(2, factory.Requests.Count);
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
}

internal sealed class NeverCalledHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => throw new InvalidOperationException("HttpClient should not be called in this test");
}

internal sealed class RecordingHttpClientFactory : IHttpClientFactory
{
    private readonly HttpStatusCode _statusCode;
    public int CallCount { get; private set; }

    public RecordingHttpClientFactory(HttpStatusCode statusCode)
        => _statusCode = statusCode;

    public HttpClient CreateClient(string name)
    {
        CallCount++;
        return new HttpClient(new FixedResponseHandler(_statusCode))
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
    }
}

internal sealed class CapturingHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<CapturedRequest> Requests { get; } = [];

    public CapturingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    public HttpClient CreateClient(string name)
        => new(new CapturingHandler(this, _responder))
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

    internal sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        Dictionary<string, object> FormFields);

    private sealed class CapturingHandler(CapturingHttpClientFactory owner, Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = await SnapshotMultipartFieldsAsync(request.Content, cancellationToken);
            owner.Requests.Add(new CapturedRequest(request.Method, request.RequestUri, fields));
            return responder(request);
        }

        private static async Task<Dictionary<string, object>> SnapshotMultipartFieldsAsync(
            HttpContent? content, CancellationToken ct)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            if (content is not MultipartFormDataContent multipart)
                return fields;

            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (string.IsNullOrEmpty(name))
                    continue;

                if (part.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                    || string.Equals(name, InactivityAttemptFormFields.File, StringComparison.Ordinal))
                {
                    fields[name] = await part.ReadAsByteArrayAsync(ct);
                }
                else
                {
                    fields[name] = await part.ReadAsStringAsync(ct);
                }
            }

            return fields;
        }
    }
}

internal sealed class FixedResponseHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    public FixedResponseHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode));
}

internal sealed class PassthroughEvidenceProtector : IEvidenceProtector
{
    public byte[] Protect(ReadOnlyMemory<byte> plaintext, Guid attemptId) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlyMemory<byte> protectedBytes, Guid attemptId) => protectedBytes.ToArray();
}
