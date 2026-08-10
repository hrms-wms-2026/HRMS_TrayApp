using System.IO.Pipes;
using System.Text;
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
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

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
    public async Task BroadcastAsync_AfterClientDisconnects_DoesNotThrow_AndOtherConnectionsStillReceive()
    {
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator());
        server.MessageReceived += RespondToPing;
        using var cts = new CancellationTokenSource();

        var first = await AttachClientAsync(server, cts.Token);
        await first.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "warmup-1" });
        await first.ReadEnvelopeAsync(cts.Token); // Pong — proves the connection is registered
        await first.DisposeAsync(); // disconnect without telling the server

        var second = await AttachClientAsync(server, cts.Token);
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
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator());
        server.MessageReceived += RespondToPing;
        var cts = new CancellationTokenSource();

        var client = await AttachClientAsync(server, cts.Token);

        // Round-trip a Ping/Pong before returning: HandleClientAsync registers the connection
        // in NamedPipeServer's dictionary before entering its read loop, so a completed reply
        // guarantees the connection is broadcast-visible — avoids a race where BroadcastAsync
        // runs before registration and silently no-ops against an empty connection set.
        await client.SendAsync(new IpcEnvelope { Type = "Ping", CorrelationId = "warmup" });
        await client.ReadEnvelopeAsync(cts.Token);

        return new Harness(server, client, cts);
    }

    /// <summary>Creates a uniquely-named pipe pair and hands the server side to HandleClientAsync,
    /// performing the nonce handshake on the client side.</summary>
    private static async Task<TestPipeClient> AttachClientAsync(NamedPipeServer server, CancellationToken ct)
    {
        var pipeName = $"ONEVO.Test.Pipe.{Guid.NewGuid():N}";
        var serverPipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var acceptTask = serverPipe.WaitForConnectionAsync(ct);
        await clientPipe.ConnectAsync((int)IoTimeout.TotalMilliseconds, ct);
        await acceptTask;

        _ = server.HandleClientAsync(serverPipe, ct); // owns and disposes serverPipe internally

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var reader = new StreamReader(clientPipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var writer = new StreamWriter(clientPipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        // Nonce handshake — mirrors ONEVO.Agent.TrayApp.Services.NamedPipeClient.AuthenticateAsync.
        var challengeLine = await ReadLineWithTimeoutAsync(reader, ct)
            ?? throw new InvalidOperationException("Pipe closed during auth challenge");
        var challenge = JsonSerializer.Deserialize<IpcEnvelope>(challengeLine)
            ?? throw new InvalidOperationException("Invalid auth challenge envelope");
        Assert.Equal(IpcMessageTypes.NonceChallenge, challenge.Type);
        var noncePayload = challenge.Payload!.Value.Deserialize<NonceChallengePayload>()!;

        var response = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            CorrelationId = challenge.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(noncePayload.Nonce))
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));

        return new TestPipeClient(clientPipe, reader, writer);
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(IoTimeout);
        return await reader.ReadLineAsync(timeoutCts.Token);
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

    /// <summary>Minimal authenticated test client: already past the nonce handshake, able to
    /// send envelopes and read the server's replies/broadcasts with a bounded timeout.</summary>
    private sealed class TestPipeClient : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public TestPipeClient(NamedPipeClientStream pipe, StreamReader reader, StreamWriter writer)
        {
            _pipe = pipe;
            _reader = reader;
            _writer = writer;
        }

        public Task SendAsync(IpcEnvelope envelope) =>
            _writer.WriteLineAsync(JsonSerializer.Serialize(envelope));

        public async Task<IpcEnvelope> ReadEnvelopeAsync(CancellationToken ct)
        {
            var line = await ReadLineWithTimeoutAsync(_reader, ct)
                ?? throw new InvalidOperationException("Pipe closed while awaiting an envelope");
            return JsonSerializer.Deserialize<IpcEnvelope>(line)
                ?? throw new InvalidOperationException("Envelope failed to deserialize");
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _writer.Dispose();
            _pipe.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
