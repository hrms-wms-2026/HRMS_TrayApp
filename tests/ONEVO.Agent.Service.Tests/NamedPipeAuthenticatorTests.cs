namespace ONEVO.Agent.Service.Tests;

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using Xunit;

public class NamedPipeAuthenticatorTests
{
    [Fact]
    public async Task MessageSentImmediatelyAfterHandshake_IsNotLost()
    {
        // ISS-006 regression: client sends NonceResponse and StatusRequest in a single write.
        await using var pair = await PipePair.CreateAsync();
        var serverReader = IpcLineCodec.CreateReader(pair.Server);
        var serverWriter = IpcLineCodec.CreateWriter(pair.Server);

        var auth = new NamedPipeAuthenticator().AuthenticateAsync(serverReader, serverWriter, CancellationToken.None);

        var clientReader = IpcLineCodec.CreateReader(pair.Client);
        var nonce = ReadNonce(await clientReader.ReadLineAsync());
        var both = Line(NonceResponse(nonce)) + Line(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest });
        await pair.Client.WriteAsync(Encoding.UTF8.GetBytes(both));
        await pair.Client.FlushAsync();

        Assert.True(await auth.WaitAsync(TimeSpan.FromSeconds(10)));
        var next = await serverReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(IpcLineCodec.TryParse(next, out var envelope));
        Assert.Equal(IpcMessageTypes.StatusRequest, envelope!.Type);
    }

    [Fact]
    public async Task ClientWritingBomPerWriter_IsStillUnderstood()
    {
        // Defensive: lines starting with a UTF-8 BOM (StreamWriter(Encoding.UTF8) per phase) still parse.
        // Writers are flushed asynchronously: setting AutoFlush = true writes the BOM synchronously,
        // which blocks on Windows until the peer reads (see the characterization test below).
        await using var pair = await PipePair.CreateAsync();
        var serverReader = IpcLineCodec.CreateReader(pair.Server);
        var serverWriter = IpcLineCodec.CreateWriter(pair.Server);

        var auth = new NamedPipeAuthenticator().AuthenticateAsync(serverReader, serverWriter, CancellationToken.None);

        var clientReader = new StreamReader(pair.Client, Encoding.UTF8, leaveOpen: true);
        var nonce = ReadNonce(await clientReader.ReadLineAsync());
        var w1 = new StreamWriter(pair.Client, Encoding.UTF8, leaveOpen: true);
        await w1.WriteLineAsync(JsonSerializer.Serialize(NonceResponse(nonce)));
        await w1.FlushAsync();
        var w2 = new StreamWriter(pair.Client, Encoding.UTF8, leaveOpen: true);
        // Not awaited yet: on Windows the write completes only once the server has read it.
        var secondWrite = WriteAndFlushAsync(w2, JsonSerializer.Serialize(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest }));

        Assert.True(await auth.WaitAsync(TimeSpan.FromSeconds(10)));
        var next = await serverReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await secondWrite.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(IpcLineCodec.TryParse(next, out var envelope));
        Assert.Equal(IpcMessageTypes.StatusRequest, envelope!.Type);
    }

    [WindowsFact]
    public void Characterization_OriginalHandshakeWriters_DeadlockOnWindows()
    {
        // Documents why IpcLineCodec must not emit a BOM (ISS-006). The original service and tray
        // each created `new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true }` before reading.
        // The AutoFlush setter writes the BOM synchronously and, on a pipe with default (zero)
        // buffer sizes, that write waits for the peer to read -> both ends wait forever.
        var name = "onevo-deadlock-" + Guid.NewGuid().ToString("N");
        var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        Task.WaitAll(server.WaitForConnectionAsync(), client.ConnectAsync(5_000));

        var serverDone = new ManualResetEventSlim();
        var clientDone = new ManualResetEventSlim();
        StartBackground(() => { _ = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true }; serverDone.Set(); });
        StartBackground(() => { _ = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true }; clientDone.Set(); });

        var completed = serverDone.Wait(TimeSpan.FromSeconds(5)) & clientDone.Wait(TimeSpan.FromSeconds(1));

        Assert.False(completed, "original handshake writers completed; the ISS-006 deadlock analysis would be wrong");
        // Blocked threads are background threads and do not keep the test host alive.
    }

    private static void StartBackground(Action action) =>
        new Thread(() => action()) { IsBackground = true }.Start();

    private static async Task WriteAndFlushAsync(StreamWriter writer, string line)
    {
        await writer.WriteLineAsync(line);
        await writer.FlushAsync();
    }

    [Fact]
    public async Task WrongNonce_IsRejected()
    {
        await using var pair = await PipePair.CreateAsync();
        var auth = new NamedPipeAuthenticator().AuthenticateAsync(
            IpcLineCodec.CreateReader(pair.Server), IpcLineCodec.CreateWriter(pair.Server), CancellationToken.None);

        var clientReader = IpcLineCodec.CreateReader(pair.Client);
        await clientReader.ReadLineAsync();
        await IpcLineCodec.CreateWriter(pair.Client).WriteLineAsync(IpcLineCodec.Serialize(NonceResponse("forged")));

        Assert.False(await auth.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task SilentClient_TimesOut_ReturnsFalse()
    {
        await using var pair = await PipePair.CreateAsync();
        var auth = new NamedPipeAuthenticator().AuthenticateAsync(
            IpcLineCodec.CreateReader(pair.Server), IpcLineCodec.CreateWriter(pair.Server), CancellationToken.None);

        await IpcLineCodec.CreateReader(pair.Client).ReadLineAsync(); // read challenge, never answer

        Assert.False(await auth.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    private static string ReadNonce(string? challengeLine)
    {
        Assert.True(IpcLineCodec.TryParse(challengeLine, out var challenge));
        Assert.Equal(IpcMessageTypes.NonceChallenge, challenge!.Type);
        return challenge.Payload!.Value.Deserialize<NonceChallengePayload>()!.Nonce;
    }

    private static IpcEnvelope NonceResponse(string nonce) => new()
    {
        Type = IpcMessageTypes.NonceResponse,
        Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(nonce))
    };

    private static string Line(IpcEnvelope envelope) => IpcLineCodec.Serialize(envelope) + "\n";
}

/// <summary>Connected named-pipe pair with a unique name (works on Windows and Unix).</summary>
internal sealed class PipePair : IAsyncDisposable
{
    public required NamedPipeServerStream Server { get; init; }
    public required NamedPipeClientStream Client { get; init; }

    public static async Task<PipePair> CreateAsync()
    {
        var name = "onevo-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync(5_000));
        return new PipePair { Server = server, Client = client };
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}
