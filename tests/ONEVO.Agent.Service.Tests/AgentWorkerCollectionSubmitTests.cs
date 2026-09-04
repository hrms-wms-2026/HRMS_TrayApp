using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// Regression coverage for the per-record-type policy gate at ingest
/// (<see cref="AgentWorker.HandleCollectionSubmitAsync"/>). Before the fix, this method gated
/// every incoming record type behind a single check of ActivitySignalEnabled — a Screenshot or
/// AppUsage record would be accepted here whenever ActivitySignalEnabled=true even if its own
/// capability flag (ScreenshotEnabled/AppUsageEnabled/CameraVerificationEnabled) was false, and
/// only got dropped later at ActivitySyncService flush time. These tests assert the ingest gate
/// now mirrors ActivitySyncService.IsAllowedByPolicy's per-type mapping exactly.
/// HandleCollectionSubmitAsync reads DeviceIdentityStore (identity.json under %ProgramData%),
/// so this class must share CredentialStoreFileCollection with every other ProgramData writer.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerCollectionSubmitTests
{
    private static AgentWorker BuildActiveWorker(PolicyCache policyCache, ActivityRecordBuffer buffer)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        stateMachine.TryTransition(MonitoringState.Active, out _);

        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — only touched if a DeviceStateSnapshot changes idle state
            stateMachine,
            policyCache,
            buffer,
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            null!, // OnevoApiClient — not touched by HandleCollectionSubmitAsync
            null!, // CredentialStore — not touched by HandleCollectionSubmitAsync
            new DeviceIdentityStore(),
            null!, // EnrollmentCoordinator — not touched by HandleCollectionSubmitAsync
            null!, // InactivityEvidenceHandler — not touched by HandleCollectionSubmitAsync
            null!  // EvidenceSpoolStore — not touched by HandleCollectionSubmitAsync
        );
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

    private static AgentPolicy MakePolicy(
        bool activitySignal = false,
        bool appUsage = false,
        bool screenshot = false,
        bool camera = false,
        bool inactivityScreenshot = false) => new()
    {
        Version = "policy-under-test",
        ActivitySignalEnabled = activitySignal,
        AppUsageEnabled = appUsage,
        ScreenshotEnabled = screenshot,
        CameraVerificationEnabled = camera,
        InactivityScreenshotEnabled = inactivityScreenshot,
        ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static async Task<CollectionRecordAckPayload> SubmitAsync(
        AgentWorker worker, params CollectionRecord[] records)
    {
        CollectionRecordAckPayload? ack = null;
        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.CollectionRecordSubmit,
            Payload = JsonSerializer.SerializeToElement(
                new CollectionRecordSubmitPayload { Records = records })
        };

        await worker.HandleCollectionSubmitAsync(envelope, reply =>
        {
            ack = reply.Payload!.Value.Deserialize<CollectionRecordAckPayload>();
            return Task.CompletedTask;
        });

        return ack!;
    }

    [Fact]
    public async Task Screenshot_Accepted_When_ScreenshotEnabled_Even_If_ActivitySignalDisabled()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: false, screenshot: true));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(CollectionRecordTypes.Screenshot, CollectionSchemaVersions.ScreenshotV1, new { });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(1, ack.AcceptedCount);
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task Screenshot_Rejected_When_ScreenshotDisabled_Even_If_ActivitySignalEnabled()
    {
        // The exact pre-fix bug: the old blanket ActivitySignalEnabled-only check would have let
        // this Screenshot record straight into the buffer.
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: true, screenshot: false));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(CollectionRecordTypes.Screenshot, CollectionSchemaVersions.ScreenshotV1, new { });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(0, ack.AcceptedCount);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task AppUsage_Rejected_When_AppUsageDisabled_Even_If_ActivitySignalEnabled()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: true, appUsage: false));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(
            CollectionRecordTypes.AppUsageSnapshot,
            CollectionSchemaVersions.AppUsageSnapshotV1,
            new AppUsageSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, ProcessName = "code.exe" });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(0, ack.AcceptedCount);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task AppUsage_Accepted_When_AppUsageEnabled_Even_If_ActivitySignalDisabled()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: false, appUsage: true));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(
            CollectionRecordTypes.AppUsageSnapshot,
            CollectionSchemaVersions.AppUsageSnapshotV1,
            new AppUsageSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, ProcessName = "code.exe" });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(1, ack.AcceptedCount);
    }

    [Fact]
    public async Task FacePhoto_Rejected_When_CameraVerificationDisabled_Even_If_ActivitySignalEnabled()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: true, camera: false));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(
            CollectionRecordTypes.FacePhoto,
            CollectionSchemaVersions.FacePhotoV1,
            new FacePhotoPayload { Format = "jpeg", Data = Convert.ToBase64String(new byte[] { 1, 2, 3 }) });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(0, ack.AcceptedCount);
    }

    [Fact]
    public async Task FacePhoto_Accepted_When_CameraVerificationEnabled()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: true, camera: true));
        var worker = BuildActiveWorker(policyCache, buffer);

        var record = MakeRecord(
            CollectionRecordTypes.FacePhoto,
            CollectionSchemaVersions.FacePhotoV1,
            new FacePhotoPayload { Format = "jpeg", Data = Convert.ToBase64String(new byte[] { 1, 2, 3 }) });
        var ack = await SubmitAsync(worker, record);

        Assert.Equal(1, ack.AcceptedCount);
    }

    [Fact]
    public async Task AllCapabilitiesDisabled_RejectsEveryRecordType()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache(); // never Set() — Current resolves to CreateDefault(), all-false
        var worker = BuildActiveWorker(policyCache, buffer);

        var records = new[]
        {
            MakeRecord(CollectionRecordTypes.ActivitySnapshot, CollectionSchemaVersions.ActivitySnapshotV1,
                new ActivitySnapshotPayload
                {
                    CapturedAt = DateTimeOffset.UtcNow,
                    KeyboardEventsCount = 1,
                    MouseEventsCount = 1,
                    ActiveSeconds = 1,
                    IdleSeconds = 0,
                    IntensityScore = 1m
                }),
            MakeRecord(CollectionRecordTypes.AppUsageSnapshot, CollectionSchemaVersions.AppUsageSnapshotV1,
                new AppUsageSnapshotPayload { CapturedAt = DateTimeOffset.UtcNow, ProcessName = "code.exe" }),
            MakeRecord(CollectionRecordTypes.Screenshot, CollectionSchemaVersions.ScreenshotV1, new { }),
            MakeRecord(CollectionRecordTypes.FacePhoto, CollectionSchemaVersions.FacePhotoV1,
                new FacePhotoPayload { Format = "jpeg", Data = Convert.ToBase64String(new byte[] { 1 }) })
        };

        var ack = await SubmitAsync(worker, records);

        Assert.Equal(0, ack.AcceptedCount);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task PolicyFlippingFromEnabledToDisabled_RejectsNextIngestForThatCapability()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(MakePolicy(activitySignal: true, screenshot: true));
        var worker = BuildActiveWorker(policyCache, buffer);

        var first = MakeRecord(CollectionRecordTypes.Screenshot, CollectionSchemaVersions.ScreenshotV1, new { });
        var ack1 = await SubmitAsync(worker, first);
        Assert.Equal(1, ack1.AcceptedCount);

        // Simulates PolicySyncService replacing the cached policy on its next refresh cycle.
        policyCache.Set(MakePolicy(activitySignal: true, screenshot: false));

        var second = MakeRecord(CollectionRecordTypes.Screenshot, CollectionSchemaVersions.ScreenshotV1, new { });
        var ack2 = await SubmitAsync(worker, second);

        Assert.Equal(0, ack2.AcceptedCount);
        Assert.Equal(1, buffer.Count); // only the first (pre-flip) record ever made it into the buffer
    }
}
