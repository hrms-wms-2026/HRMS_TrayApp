namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

/// <summary>
/// Drives the real <see cref="NamedPipeClient"/> against an in-test pipe server on a unique pipe
/// name, so a locally installed Agent Service is never touched.
/// </summary>
public sealed class NamedPipeClientConnectionTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task Request_WhenNotConnected_FailsFastInsteadOfWaitingForTimeout()
    {
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, NewPipeName());

        var sw = Stopwatch.StartNew();
        var result = await client.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task LiveConnectionDrop_SendFailsFast_AndClientReconnectsQuickly()
    {
        var pipeName = NewPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, pipeName);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnDisconnected += () => disconnected.TrySetResult();

        await using (var first = CreateServer(pipeName))
        {
            await client.StartAsync(cts.Token);
            await AcceptAndHandshakeAsync(first, cts.Token);
        } // the server closes the live connection here

        await disconnected.Task.WaitAsync(cts.Token);

        // No IOException from writing into the dead pipe, and no wait for a reply that can't come.
        var sw = Stopwatch.StartNew();
        var result = await client.SendLifecycleAsync(LifecycleAction.ClockIn, cts.Token);
        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed}");

        // Well inside the 15s steady-state delay: a live drop goes straight back to the fast burst.
        await using var second = CreateServer(pipeName);
        await second.WaitForConnectionAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Request_AfterDispose_DoesNotThrow()
    {
        var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, NewPipeName());
        await client.StartAsync(CancellationToken.None);
        await client.DisposeAsync();

        // A late UI call during shutdown must not hit a disposed write lock.
        var result = await client.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        Assert.Null(result);
    }

    private static string NewPipeName() => $"ONEVO.Agent.Tests.{Guid.NewGuid():N}";

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static async Task AcceptAndHandshakeAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await server.WaitForConnectionAsync(ct);
        using var reader = new StreamReader(server, Utf8, false, 1024, leaveOpen: true);
        await using var writer = new StreamWriter(server, Utf8, 1024, leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceChallenge,
            CorrelationId = "challenge",
            Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload("nonce"))
        }));
        await reader.ReadLineAsync(ct); // NonceResponse
        await reader.ReadLineAsync(ct); // StatusRequest, sent right after connecting
    }
}
