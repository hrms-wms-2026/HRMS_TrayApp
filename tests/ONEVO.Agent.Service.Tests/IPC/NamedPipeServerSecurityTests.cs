using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

/// <summary>
/// Real, uniquely-named pipe pairs (see NamedPipeServerBroadcastTests for why not Constants.PipeName).
/// The test process is the client, so to the real verifier it looks exactly like a script that
/// connects and echoes the nonce — the attack this verifier exists to stop.
/// </summary>
public class NamedPipeServerSecurityTests
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task NonTrayClient_IsDisconnectedBeforeTheHandshake()
    {
        var verifier = new IpcClientVerifier(
            NullLogger<IpcClientVerifier>.Instance,
            new WindowsClientProcessInspector(),
            Options.Create(new AgentOptions { AllowIpcClientFromAnyPath = true }));
        var (server, client, cts) = await ConnectAsync(verifier);
        using (cts)
        await using (client)
        {
            using var reader = new StreamReader(client, Utf8, false, 1024, leaveOpen: true);

            // No NonceChallenge: the pipe is closed straight away.
            Assert.Null(await reader.ReadLineAsync(cts.Token));
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task WindowsInspector_ReportsTheConnectedClientProcess()
    {
        IpcClientProcess? seen = null;
        var verifier = new RecordingVerifier(pipe => seen = new WindowsClientProcessInspector().Inspect(pipe));
        var (server, client, cts) = await ConnectAsync(verifier);
        using (cts)
        await using (client)
        {
            await verifier.Called.WaitAsync(IoTimeout);
            await server.DisposeAsync();
        }

        using var self = Process.GetCurrentProcess();
        Assert.NotNull(seen);
        Assert.Equal(self.Id, seen!.ProcessId);
        Assert.Equal(self.MainModule!.FileName, seen.ExecutablePath, ignoreCase: true);
        Assert.Equal(self.SessionId, seen.SessionId);
    }

    [Fact]
    public async Task OversizedMessage_IsDiscarded_AndTheConnectionKeepsWorking()
    {
        var (server, client, cts) = await ConnectAsync(TrustAllClients.Instance);
        server.MessageReceived += (envelope, reply) => envelope.Type == "Ping"
            ? reply(new IpcEnvelope { Type = "Pong", CorrelationId = envelope.CorrelationId })
            : Task.CompletedTask;
        using (cts)
        await using (client)
        {
            using var reader = new StreamReader(client, Utf8, false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(client, Utf8, 1024, leaveOpen: true) { AutoFlush = true };
            await HandshakeAsync(reader, writer, cts.Token);

            await writer.WriteLineAsync(new string('x', 200_000));
            await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcEnvelope { Type = "Ping", CorrelationId = "after" }));

            var pong = JsonSerializer.Deserialize<IpcEnvelope>((await reader.ReadLineAsync(cts.Token))!)!;
            Assert.Equal("Pong", pong.Type);
            Assert.Equal("after", pong.CorrelationId);
            await server.DisposeAsync();
        }
    }

    private static async Task<(NamedPipeServer Server, NamedPipeClientStream Client, CancellationTokenSource Cts)>
        ConnectAsync(IIpcClientVerifier verifier)
    {
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), verifier);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pipeName = $"ONEVO.Test.Pipe.{Guid.NewGuid():N}";
        var serverPipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var acceptTask = serverPipe.WaitForConnectionAsync(cts.Token);
        await clientPipe.ConnectAsync((int)IoTimeout.TotalMilliseconds, cts.Token);
        await acceptTask;
        _ = server.HandleClientAsync(serverPipe, cts.Token); // owns and disposes serverPipe

        return (server, clientPipe, cts);
    }

    private static async Task HandshakeAsync(StreamReader reader, StreamWriter writer, CancellationToken ct)
    {
        var challenge = JsonSerializer.Deserialize<IpcEnvelope>((await reader.ReadLineAsync(ct))!)!;
        Assert.Equal(IpcMessageTypes.NonceChallenge, challenge.Type);
        var nonce = challenge.Payload!.Value.Deserialize<NonceChallengePayload>()!.Nonce;
        await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            CorrelationId = challenge.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(nonce))
        }));
    }

    private sealed class RecordingVerifier(Action<NamedPipeServerStream> onCheck) : IIpcClientVerifier
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public bool IsTrusted(NamedPipeServerStream pipe)
        {
            try { onCheck(pipe); }
            finally { _called.TrySetResult(); }
            return false;
        }
    }
}
