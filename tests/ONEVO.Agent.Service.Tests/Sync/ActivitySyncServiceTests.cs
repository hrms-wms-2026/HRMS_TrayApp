using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Shared.Models;
using Xunit;

#pragma warning disable CA1001 // RecordingHttpClientFactory creates HttpClient (test code, no disposal needed)

namespace ONEVO.Agent.Service.Tests.Sync;

public class ActivitySyncServiceTests
{
    private static ActivitySyncService Build(
        ActivityRecordBuffer buffer,
        IHttpClientFactory? factory = null,
        AgentOptions? options = null)
    {
        return new ActivitySyncService(
            NullLogger<ActivitySyncService>.Instance,
            buffer,
            new CredentialStore(),   // no JWT on disk in test env
            factory ?? new NeverCalledHttpClientFactory(),
            Options.Create(options ?? new AgentOptions { ApiBaseUrl = "https://api.example.com" }));
    }

    private static CollectionRecord MakeRecord(string type, string schema, object payload) => new()
    {
        EventId          = Guid.NewGuid().ToString("N"),
        RecordType       = type,
        SchemaVersion    = schema,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId         = "test",
        Payload          = JsonSerializer.SerializeToElement(payload)
    };

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

        // CredentialStore has no JWT in test environment → should requeue
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
        var buffer  = ActivityRecordBuffer.CreateInMemory();
        var svc     = Build(buffer, factory);

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

        var batch = buffer.DequeueBatch(10);
        Assert.Equal(3, batch.Count);
        Assert.Contains(batch, r => r.RecordType == CollectionRecordTypes.ActivitySnapshot);
        Assert.Contains(batch, r => r.RecordType == CollectionRecordTypes.AppUsageSnapshot);
        Assert.Contains(batch, r => r.RecordType == CollectionRecordTypes.DeviceStateSnapshot);
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

internal sealed class FixedResponseHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    public FixedResponseHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode));
}
