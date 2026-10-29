namespace ONEVO.Agent.Service.Tests;

using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

public class NamedPipeServerTests
{
    [Fact]
    public async Task DisposeAsync_Twice_DoesNotThrow()
    {
        // ISS-018 regression: AgentWorker.StopAsync and the DI container both dispose the server.
        var name = UniqueName();
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), () => PlainPipe(name));
        await server.StartAsync(CancellationToken.None);

        await server.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task AcceptLoop_SurvivesPipeCreationFailure()
    {
        // ISS-018 regression: a throwing pipe creation used to end the accept loop silently.
        var name = UniqueName();
        var calls = 0;
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), () =>
            Interlocked.Increment(ref calls) == 1 ? throw new IOException("transient") : PlainPipe(name));
        ReplyToStatusRequests(server);
        await server.StartAsync(CancellationToken.None);

        var state = await RoundTripStatusAsync(name);

        Assert.Equal(MonitoringState.Stopped, state);
        Assert.True(calls >= 2);
    }

    [Fact]
    public async Task ClientDroppingDuringHandshake_DoesNotStopServer()
    {
        var name = UniqueName();
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), () => PlainPipe(name));
        ReplyToStatusRequests(server);
        await server.StartAsync(CancellationToken.None);

        await using (var rude = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            await rude.ConnectAsync(5_000); // disconnect without answering the challenge

        Assert.Equal(MonitoringState.Stopped, await RoundTripStatusAsync(name));
    }

    [WindowsFact]
    public async Task ProductionPipe_WithAcl_CompletesHandshakeAndStatusRoundTrip()
    {
        // Uses the real CreateSecurePipe (ACL) and the production pipe name; Windows only.
        await using var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator());
        ReplyToStatusRequests(server);
        await server.StartAsync(CancellationToken.None);

        Assert.Equal(MonitoringState.Stopped, await RoundTripStatusAsync(Constants.PipeName));
    }

    private static void ReplyToStatusRequests(NamedPipeServer server) =>
        server.MessageReceived += async (envelope, reply) =>
        {
            if (envelope.Type == IpcMessageTypes.StatusRequest)
                await reply(new IpcEnvelope
                {
                    Type = IpcMessageTypes.StatusResponse,
                    CorrelationId = envelope.CorrelationId,
                    Payload = JsonSerializer.SerializeToElement(
                        new StatusResponsePayload(MonitoringState.Stopped, DateTimeOffset.UtcNow))
                });
        };

    /// <summary>Acts like the tray: handshake, then StatusRequest sent in the same write as the nonce response.</summary>
    private static async Task<MonitoringState> RoundTripStatusAsync(string name)
    {
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);
        var reader = IpcLineCodec.CreateReader(client);
        var writer = IpcLineCodec.CreateWriter(client);

        Assert.True(IpcLineCodec.TryParse(await reader.ReadLineAsync(), out var challenge));
        var nonce = challenge!.Payload!.Value.Deserialize<NonceChallengePayload>()!.Nonce;
        var response = IpcLineCodec.Serialize(new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(nonce))
        });
        var request = IpcLineCodec.Serialize(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest });
        await writer.WriteAsync(response + "\n" + request + "\n");

        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(IpcLineCodec.TryParse(line, out var status));
        Assert.Equal(IpcMessageTypes.StatusResponse, status!.Type);
        return status.Payload!.Value.Deserialize<StatusResponsePayload>()!.State;
    }

    private static string UniqueName() => "onevo-srv-test-" + Guid.NewGuid().ToString("N");

    private static NamedPipeServerStream PlainPipe(string name) => new(name, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}

/// <summary>Fact that only runs on Windows (e.g. pipe ACLs, DPAPI, file ACLs).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Requires Windows";
    }
}
