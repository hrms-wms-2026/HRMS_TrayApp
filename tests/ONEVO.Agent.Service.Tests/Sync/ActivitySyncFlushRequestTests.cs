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
/// AUD-033: a finished screenshot was meant to upload immediately, but the flush hook was never
/// registered in DI, so it waited for the ingest interval. RequestFlush wakes the sync loop early
/// without the caller awaiting any HTTP (it is called from the pipe read loop).
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class ActivitySyncFlushRequestTests
{
    private static CollectionRecord WorkSession() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.WorkSession,
        SchemaVersion = CollectionSchemaVersions.WorkSessionV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "test",
        Payload = JsonSerializer.SerializeToElement(new WorkSessionPayload
        {
            SessionId = Guid.NewGuid(),
            ClockInAt = DateTimeOffset.UtcNow.AddHours(-8),
            ClockOutAt = DateTimeOffset.UtcNow,
            AccumulatedBreak = TimeSpan.FromMinutes(30),
            AccumulatedWork = TimeSpan.FromHours(7),
            BreakSessionCount = 1,
            ScheduleDisplay = "09:00 AM – 06:00 PM"
        })
    };

    private static async Task<int> RunAsync(bool requestFlush, TimeSpan observeFor)
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var credentials = TestStores.NewCredentialStore();
        credentials.StoreDeviceJwt("test-jwt");
        var factory = new RecordingHttpClientFactory(HttpStatusCode.OK);
        var sync = new ActivitySyncService(
            NullLogger<ActivitySyncService>.Instance,
            buffer,
            credentials,
            factory,
            new PassthroughEvidenceProtector(),
            new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
            Options.Create(new AgentOptions { ApiBaseUrl = "https://api.example.com", IngestIntervalSeconds = 3600 }));
        try
        {
            await sync.StartAsync(CancellationToken.None);
            await Task.Delay(200); // the loop's first (empty) flush runs at start

            Assert.True(buffer.TryEnqueue(WorkSession()));
            if (requestFlush)
                sync.RequestFlush();

            var deadline = DateTime.UtcNow + observeFor;
            while (factory.CallCount == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            return factory.CallCount;
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task RequestFlush_UploadsWithoutWaitingForTheIngestInterval()
    {
        Assert.True(await RunAsync(requestFlush: true, observeFor: TimeSpan.FromSeconds(5)) > 0);
    }

    [Fact]
    public async Task WithoutARequest_RowsWaitForTheIngestInterval()
    {
        Assert.Equal(0, await RunAsync(requestFlush: false, observeFor: TimeSpan.FromSeconds(1)));
    }
}
