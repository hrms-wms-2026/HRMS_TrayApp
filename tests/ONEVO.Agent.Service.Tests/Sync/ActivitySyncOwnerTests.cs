using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Sync;

/// <summary>
/// AUD-004: rows queued by one employee must not be uploaded with another employee's device token
/// after a logout/lock and re-enrollment on the same PC (possibly another tenant).
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class ActivitySyncOwnerTests
{
    private const string EmployeeA = "tenant-a:user-a";

    private static CollectionRecord Snapshot() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
        DeviceId = "device",
        Payload = JsonSerializer.SerializeToElement(new ActivitySnapshotPayload
        {
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            KeyboardEventsCount = 1,
            MouseEventsCount = 1,
            ActiveSeconds = 60,
            IdleSeconds = 0,
            IntensityScore = 0.1m
        })
    };

    private static async Task FlushWithTokenAsync(ActivityRecordBuffer buffer, string jwt, IHttpClientFactory factory)
    {
        var credentials = TestStores.NewCredentialStore();
        try
        {
            credentials.StoreDeviceJwt(jwt);
            var sync = new ActivitySyncService(
                NullLogger<ActivitySyncService>.Instance,
                buffer,
                credentials,
                factory,
                new PassthroughEvidenceProtector(),
                new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
                Options.Create(new AgentOptions { ApiBaseUrl = "https://api.example.com" }));
            await sync.FlushAsync(CancellationToken.None);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task RowsOfAnotherEmployee_AreQuarantinedNotSent()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(ownerKeyProvider: () => EmployeeA);
        Assert.True(buffer.TryEnqueue(Snapshot()));

        await FlushWithTokenAsync(
            buffer, FakeJwt.ForEmployee(tenantId: "tenant-b", userId: "user-b"), new NeverCalledHttpClientFactory());

        var health = buffer.GetHealth();
        Assert.Equal(0, health.Pending);
        Assert.Equal(1, health.Quarantined);
    }

    [Fact]
    public async Task RowsOfTheSameEmployee_AreSentAfterReEnrollment()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(ownerKeyProvider: () => EmployeeA);
        Assert.True(buffer.TryEnqueue(Snapshot()));
        var factory = new RecordingHttpClientFactory(HttpStatusCode.OK);

        // New device registration (sub) for the same tenant/user, as after a re-enrollment.
        await FlushWithTokenAsync(buffer, FakeJwt.ForEmployee(tenantId: "tenant-a", userId: "user-a"), factory);

        Assert.True(factory.CallCount > 0);
        Assert.Equal(0, buffer.GetHealth().Pending);
        Assert.Equal(0, buffer.GetHealth().Quarantined);
    }

    [Fact]
    public async Task RowsWithoutOwner_KeepTheirPreviousBehaviour()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(Snapshot()));
        var factory = new RecordingHttpClientFactory(HttpStatusCode.OK);

        await FlushWithTokenAsync(buffer, FakeJwt.ForEmployee(tenantId: "tenant-b", userId: "user-b"), factory);

        Assert.True(factory.CallCount > 0);
        Assert.Equal(0, buffer.GetHealth().Pending);
    }
}
