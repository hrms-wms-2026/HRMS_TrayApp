using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

/// <summary>
/// Exercises NamedPipeServer's broadcast path over a real, in-process Named Pipe pair.
///
/// Deliberately does NOT go through NamedPipeServer.StartAsync()/Constants.PipeName: that pipe
/// name is fixed, and a dev machine may already have a real ONEVO Agent Service instance bound
/// to it (Windows allows multiple server instances of the same name, so a test connecting to
/// the well-known name could nondeterministically land on the live service instead of the test
/// harness). Instead, each test creates its own uniquely-named pipe pair and calls the internal
/// HandleClientAsync(...) directly (see InternalsVisibleTo in ONEVO.Agent.Service.csproj) — same
/// authentication, read loop, and connection-tracking code path, zero collision risk.
///
/// Every blocking read below goes through a bounded CancellationTokenSource so a broken
/// assumption (e.g. a missing write) fails the test promptly instead of hanging the run.
/// </summary>
public class NamedPipeServerBroadcastTests
{
    [Fact]
    public async Task BroadcastAsync_DeliversToAuthenticatedConnection()
    {
        await using var harness = await ConnectedClientAsync();

        var policy = SamplePolicy("broadcast-v1");

        // Start the read BEFORE broadcasting. This pipe (zero-size in/out buffers — see
        // NamedPipeServer.CreateSecurePipe) only completes a server-side write once a reader
        // is actively consuming it, exactly like the real Tray client whose read loop is
        // always mid-ReadLineAsync. Broadcast-then-read here would deadlock the write.
        var readTask = harness.Client.ReadEnvelopeAsync(harness.Cts.Token);
        await harness.Server.BroadcastAsync(PolicyPushEnvelope(policy), harness.Cts.Token);
        var received = await readTask;

        Assert.Equal(IpcMessageTypes.PolicyPush, received.Type);
        var payload = received.Payload!.Value.Deserialize<PolicyPushPayload>();
        Assert.Equal("broadcast-v1", payload!.Policy.Version);
    }

    [Fact]
    public async Task BroadcastAsync_ConcurrentWithRequestReply_DoesNotInterleaveJsonLines()
    {
        await using var harness = await ConnectedClientAsync();

        // Pad the payload well past a single internal buffer flush — with the write lock
        // missing, concurrent writers would tear each other's output mid-line and every
        // ReadLineAsync below would either throw JsonException or return truncated content.
        var largeVersion = new string('V', 40_000);
        var policy = SamplePolicy(largeVersion);
        var broadcastEnvelope = PolicyPushEnvelope(policy);

        const int broadcastCount = 6;

        // Drain concurrently with the writes below, on a separate task — as in the previous
        // test, this pipe only completes a write once a reader is actively consuming it, so
        // reading strictly after issuing every write would deadlock the first one.
        var drainTask = Task.Run(async () =>
        {
            var results = new List<IpcEnvelope>();
            for (var i = 0; i < broadcastCount + 1; i++)
            {
                // A corrupted/interleaved line fails here (JsonException) rather than silently
                // passing — that is the assertion this test exists for.
                results.Add(await harness.Client.ReadEnvelopeAsync(harness.Cts.Token));
            }
            return results;
        });

        var broadcastTasks = Enumerable.Range(0, broadcastCount)
            .Select(_ => harness.Server.BroadcastAsync(broadcastEnvelope, harness.Cts.Token))
            .ToArray();
        var pingTask = harness.Client.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "concurrent-ping" });

        await Task.WhenAll(broadcastTasks.Append(pingTask));
        var receivedEnvelopes = await drainTask;

        var pushCount = 0;
        var pongCount = 0;
        foreach (var envelope in receivedEnvelopes)
        {
            if (envelope.Type == IpcMessageTypes.PolicyPush)
            {
                var payload = envelope.Payload!.Value.Deserialize<PolicyPushPayload>();
                Assert.Equal(largeVersion, payload!.Policy.Version);
                pushCount++;
            }
            else if (envelope.Type == "Pong")
            {
                pongCount++;
            }
        }

        Assert.Equal(broadcastCount, pushCount);
        Assert.Equal(1, pongCount);
    }

    [Fact]
    public async Task DisposeAsync_Twice_DoesNotThrow()
    {
        // DI disposes the singleton once as NamedPipeServer and again as IIpcBroadcaster; the
        // second call used to crash host shutdown with ObjectDisposedException.
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), TrustAllClients.Instance);
        await server.StartAsync(CancellationToken.None);
        await server.DisposeAsync();

        var ex = await Record.ExceptionAsync(() => server.DisposeAsync().AsTask());

        Assert.Null(ex);
    }

    [Fact]
    public async Task BroadcastAsync_AfterClientDisconnects_DoesNotThrow_AndOtherConnectionsStillReceive()
    {
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), TrustAllClients.Instance);
        server.MessageReceived += RespondToPing;
        using var cts = new CancellationTokenSource();

        var first = await TestPipeConnection.AttachClientAsync(server, cts.Token);
        await first.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "warmup-1" });
        await first.ReadEnvelopeAsync(cts.Token); // Pong — proves the connection is registered
        await first.DisposeAsync(); // disconnect without telling the server

        var second = await TestPipeConnection.AttachClientAsync(server, cts.Token);
        await second.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "warmup-2" });
        await second.ReadEnvelopeAsync(cts.Token); // Pong

        var policy = SamplePolicy("after-disconnect-v1");
        var readTask = second.ReadEnvelopeAsync(cts.Token); // started before broadcasting — see above
        var ex = await Record.ExceptionAsync(
            () => server.BroadcastAsync(PolicyPushEnvelope(policy), cts.Token));
        Assert.Null(ex);

        var received = await readTask;
        Assert.Equal(IpcMessageTypes.PolicyPush, received.Type);

        await second.DisposeAsync();
        await cts.CancelAsync();
        await server.DisposeAsync();
    }

    private static Task RespondToPing(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply) =>
        envelope.Type == "Ping"
            ? reply(new IpcEnvelope { Type = "Pong", CorrelationId = envelope.CorrelationId })
            : Task.CompletedTask;

    private static IpcEnvelope PolicyPushEnvelope(AgentPolicy policy) => new()
    {
        Type = IpcMessageTypes.PolicyPush,
        Payload = JsonSerializer.SerializeToElement(new PolicyPushPayload { Policy = policy })
    };

    private static AgentPolicy SamplePolicy(string version) => new()
    {
        Version = version,
        ActivitySignalEnabled = true,
        AppUsageEnabled = true,
        ScreenshotEnabled = true,
        InactivityScreenshotEnabled = true,
        CameraVerificationEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static async Task<Harness> ConnectedClientAsync()
    {
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), TrustAllClients.Instance);
        server.MessageReceived += RespondToPing;
        var cts = new CancellationTokenSource();

        var client = await TestPipeConnection.AttachClientAsync(server, cts.Token);

        // Round-trip a Ping/Pong before returning: HandleClientAsync registers the connection
        // in NamedPipeServer's dictionary before entering its read loop, so a completed reply
        // guarantees the connection is broadcast-visible — avoids a race where BroadcastAsync
        // runs before registration and silently no-ops against an empty connection set.
        await client.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "warmup" });
        await client.ReadEnvelopeAsync(cts.Token);

        return new Harness(server, client, cts);
    }

    private sealed record Harness(NamedPipeServer Server, TestPipeClient Client, CancellationTokenSource Cts) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Cts.CancelAsync();
            Cts.Dispose();
            await Server.DisposeAsync();
        }
    }
}
