using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

/// <summary>
/// Connects a test client to <see cref="NamedPipeServer.HandleClientAsync"/> over a real,
/// uniquely-named pipe pair, past the nonce handshake. Shared by the IPC server tests; see
/// <see cref="NamedPipeServerBroadcastTests"/> for why the fixed production pipe name is avoided.
/// </summary>
internal static class TestPipeConnection
{
    internal static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Creates a uniquely-named pipe pair and hands the server side to HandleClientAsync,
    /// performing the nonce handshake on the client side.</summary>
    internal static async Task<TestPipeClient> AttachClientAsync(NamedPipeServer server, CancellationToken ct)
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

    internal static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(IoTimeout);
        return await reader.ReadLineAsync(timeoutCts.Token);
    }
}

/// <summary>Minimal authenticated test client: already past the nonce handshake, able to
/// send envelopes and read the server's replies/broadcasts with a bounded timeout.</summary>
internal sealed class TestPipeClient : IAsyncDisposable
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
        var line = await TestPipeConnection.ReadLineWithTimeoutAsync(_reader, ct)
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
