namespace ONEVO.Agent.Service.Tests;

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

public class ActivitySyncServiceTests
{
    private const string TestJwt = "test-device-token"; // synthetic, not a real credential

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task Success_RemovesRecords_AndSendsContractShapedRequest(HttpStatusCode status)
    {
        var buffer = BufferWith(Snapshot("a"), Snapshot("b"));
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        await CreateService(buffer, handler).FlushAsync(CancellationToken.None);

        Assert.Equal(0, buffer.Count);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/monitoring/activity/snapshots", request.Path);
        Assert.Equal("Bearer " + TestJwt, request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        var snapshots = body.RootElement.GetProperty("snapshots");
        Assert.Equal(2, snapshots.GetArrayLength());
        var first = snapshots[0];
        foreach (var field in new[] { "captured_at", "keyboard_events_count", "mouse_events_count",
                     "active_seconds", "idle_seconds", "intensity_score", "foreground_process_name" })
            Assert.True(first.TryGetProperty(field, out _), $"missing wire field {field}");
        Assert.Equal(12, first.GetProperty("keyboard_events_count").GetInt32());
        Assert.False(first.TryGetProperty("tenant_id", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthRejection_KeepsRecords(HttpStatusCode status)
    {
        // ISS-007 regression: records used to be dropped on 401/403.
        var buffer = BufferWith(Snapshot("a"), Snapshot("b"));

        await CreateService(buffer, new StubHandler(_ => new HttpResponseMessage(status))).FlushAsync(CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, Drain(buffer));
    }

    [Fact]
    public async Task ServerError_KeepsRecords()
    {
        var buffer = BufferWith(Snapshot("a"));

        await CreateService(buffer, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
            .FlushAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task NetworkFailure_KeepsRecords()
    {
        var buffer = BufferWith(Snapshot("a"));

        await CreateService(buffer, new StubHandler(_ => throw new HttpRequestException("connection reset")))
            .FlushAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task UnexpectedException_KeepsRecords()
    {
        // ISS-008 regression: an exception after dequeue (here: credential file unreadable) lost the batch.
        var buffer = BufferWith(Snapshot("a"), Snapshot("b"));
        var credentials = new FakeCredentials { ReadException = new IOException("file locked") };
        var service = CreateService(buffer, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), credentials);

        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync(CancellationToken.None));

        Assert.Equal(new[] { "a", "b" }, Drain(buffer));
    }

    [Fact]
    public async Task CancelledDuringSend_KeepsRecords()
    {
        var buffer = BufferWith(Snapshot("a"));
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(_ => { cts.Cancel(); throw new TaskCanceledException(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(buffer, handler).FlushAsync(cts.Token));

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public async Task FailedBatch_ReturnsToFront_PreservingCaptureOrder()
    {
        var buffer = BufferWith(Snapshot("a"), Snapshot("b"));
        var service = CreateService(buffer, new StubHandler(_ =>
        {
            buffer.TryEnqueue(Snapshot("c")); // captured while the upload is in flight
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }));

        await service.FlushAsync(CancellationToken.None);

        Assert.Equal(new[] { "a", "b", "c" }, Drain(buffer));
    }

    [Fact]
    public async Task NoCredential_KeepsRecords_WithoutCallingBackend()
    {
        var buffer = BufferWith(Snapshot("a"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await CreateService(buffer, handler, new FakeCredentials { Jwt = null }).FlushAsync(CancellationToken.None);

        Assert.Equal(1, buffer.Count);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CorruptRecords_AreQuarantined_ValidOnesSent_AndOnlyValidOnesKeptOnFailure()
    {
        var corrupt = Snapshot("bad") with { Payload = JsonSerializer.SerializeToElement(new { KeyboardEventsCount = "x" }) };
        var unknown = Snapshot("other") with { RecordType = "unknown_type" };
        var buffer = BufferWith(corrupt, Snapshot("good"), unknown);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await CreateService(buffer, handler).FlushAsync(CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(1, body.RootElement.GetProperty("snapshots").GetArrayLength());
        Assert.Equal(new[] { "good" }, Drain(buffer));
    }

    [Fact]
    public async Task StopAsync_FinishesWithinBound_WhenBackendHangs()
    {
        // ISS-021 regression: the final flush on stop used CancellationToken.None.
        var buffer = BufferWith(Snapshot("a"));
        var handler = StubHandler.Async(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(buffer, handler);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        var stop = service.StopAsync(CancellationToken.None);

        await stop.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, buffer.Count);
    }

    private static ActivitySyncService CreateService(
        ActivityRecordBuffer buffer, StubHandler handler, FakeCredentials? credentials = null) =>
        new(NullLogger<ActivitySyncService>.Instance,
            buffer,
            credentials ?? new FakeCredentials(),
            new StubHttpClientFactory(handler),
            Options.Create(new AgentOptions { ApiBaseUrl = "https://api.test.invalid", IngestIntervalSeconds = 30 }));

    private static ActivityRecordBuffer BufferWith(params CollectionRecord[] records)
    {
        var buffer = new ActivityRecordBuffer();
        foreach (var r in records) Assert.True(buffer.TryEnqueue(r));
        return buffer;
    }

    private static string[] Drain(ActivityRecordBuffer buffer) =>
        buffer.DequeueBatch(int.MaxValue).Select(r => r.EventId).ToArray();

    internal static CollectionRecord Snapshot(string eventId) => new()
    {
        EventId = eventId,
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        DeviceId = "test-device",
        CaptureTimestamp = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        Payload = JsonSerializer.SerializeToElement(new ActivitySnapshotPayload
        {
            CapturedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            KeyboardEventsCount = 12,
            MouseEventsCount = 3,
            ActiveSeconds = 50,
            IdleSeconds = 10,
            IntensityScore = 25m,
            ForegroundProcessName = "test.exe"
        })
    };
}

internal sealed class FakeCredentials : ICredentialStore
{
    public string? Jwt { get; set; } = "test-device-token";
    public Exception? ReadException { get; set; }

    public string? ReadDeviceJwt() => ReadException is null ? Jwt : throw ReadException;
    public void StoreDeviceJwt(string jwt) => Jwt = jwt;
    public void ClearDeviceJwt() => Jwt = null;
}

internal sealed record CapturedRequest(HttpMethod Method, string Path, string? Authorization, string Body);

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _respond;
    public List<CapturedRequest> Requests { get; } = [];

    public StubHandler(Func<CancellationToken, HttpResponseMessage> respond) =>
        _respond = ct => Task.FromResult(respond(ct));

    private StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond, bool _) => _respond = respond;

    public static StubHandler Async(Func<CancellationToken, Task<HttpResponseMessage>> respond) => new(respond, true);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(), body));
        return await _respond(ct);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test.invalid") };
}
