using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Sync;

/// <summary>
/// AUD-002: the policy cache is memory-only and the server policy is valid for one hour. A Service
/// restart before the first fetch, or more than an hour offline, leaves no valid policy — that must
/// defer sending, not quarantine rows that were captured while a real policy allowed them.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class ActivitySyncPolicyAvailabilityTests
{
    private static CollectionRecord ActivitySnapshot() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow.AddMinutes(-90),
        DeviceId = "test",
        Payload = JsonSerializer.SerializeToElement(new ActivitySnapshotPayload
        {
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-90),
            KeyboardEventsCount = 10,
            MouseEventsCount = 5,
            ActiveSeconds = 60,
            IdleSeconds = 0,
            IntensityScore = 0.5m
        })
    };

    private static AgentPolicy ValidPolicy(bool activityEnabled, DateTimeOffset validUntil) => new()
    {
        Version = "policy-v1",
        ActivitySignalEnabled = activityEnabled,
        ValidUntil = validUntil
    };

    private static async Task FlushOnceAsync(ActivityRecordBuffer buffer, PolicyCache policyCache, IHttpClientFactory factory)
    {
        var credentials = TestStores.NewCredentialStore();
        try
        {
            credentials.StoreDeviceJwt("test-jwt");
            var sync = new ActivitySyncService(
                NullLogger<ActivitySyncService>.Instance,
                buffer,
                credentials,
                factory,
                new PassthroughEvidenceProtector(),
                new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
                Options.Create(new AgentOptions { ApiBaseUrl = "https://api.example.com" }),
                policyCache);

            await sync.FlushAsync(CancellationToken.None);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task PolicyNeverFetched_LeavesBufferedRowsPending()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(ActivitySnapshot()));

        await FlushOnceAsync(buffer, new PolicyCache(), new NeverCalledHttpClientFactory());

        var health = buffer.GetHealth();
        Assert.Equal(1, health.Pending);
        Assert.Equal(0, health.Quarantined);
    }

    [Fact]
    public async Task PolicyExpired_LeavesBufferedRowsPending()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(ActivitySnapshot()));
        var policyCache = new PolicyCache();
        policyCache.Set(ValidPolicy(activityEnabled: true, validUntil: DateTimeOffset.UtcNow.AddMinutes(-1)));

        await FlushOnceAsync(buffer, policyCache, new NeverCalledHttpClientFactory());

        var health = buffer.GetHealth();
        Assert.Equal(1, health.Pending);
        Assert.Equal(0, health.Quarantined);
    }

    [Fact]
    public async Task ValidPolicyArrivesLater_DeferredRowsAreSent()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(ActivitySnapshot()));
        var policyCache = new PolicyCache();
        await FlushOnceAsync(buffer, policyCache, new NeverCalledHttpClientFactory());

        policyCache.Set(ValidPolicy(activityEnabled: true, validUntil: DateTimeOffset.UtcNow.AddHours(1)));
        var factory = new RecordingHttpClientFactory(HttpStatusCode.OK);
        await FlushOnceAsync(buffer, policyCache, factory);

        Assert.True(factory.CallCount > 0);
        Assert.Equal(0, buffer.GetHealth().Pending);
    }

    // A valid, current policy that turns a capability off still quarantines (unchanged behaviour).
    [Fact]
    public async Task ValidPolicyWithCapabilityOff_StillQuarantines()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(ActivitySnapshot()));
        var policyCache = new PolicyCache();
        policyCache.Set(ValidPolicy(activityEnabled: false, validUntil: DateTimeOffset.UtcNow.AddHours(1)));

        await FlushOnceAsync(buffer, policyCache, new NeverCalledHttpClientFactory());

        var health = buffer.GetHealth();
        Assert.Equal(0, health.Pending);
        Assert.Equal(1, health.Quarantined);
    }
}
