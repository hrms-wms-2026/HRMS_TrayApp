namespace ONEVO.Agent.TrayApp.Tests;

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

public class NamedPipeClientTests
{
    [Fact]
    public async Task MessageSentTogetherWithChallenge_IsDelivered()
    {
        // ISS-006 regression (client side): the client's handshake reader must not swallow
        // data the service sends in the same write as the challenge.
        var fake = new FakeService();
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, fake.PipeName);
        var state = new TaskCompletionSource<MonitoringState>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnStateReceived += s => state.TrySetResult(s);

        var serve = fake.ServeOnceAsync(async (pipe, reader) =>
        {
            var challenge = IpcLineCodec.Serialize(new IpcEnvelope
            {
                Type = IpcMessageTypes.NonceChallenge,
                Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload("n"))
            });
            var push = IpcLineCodec.Serialize(new IpcEnvelope
            {
                Type = IpcMessageTypes.StatusResponse,
                Payload = JsonSerializer.SerializeToElement(
                    new StatusResponsePayload(MonitoringState.Active, DateTimeOffset.UtcNow))
            });
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(challenge + "\n" + push + "\n"));
            await pipe.FlushAsync();
            await reader.ReadLineAsync(); // nonce response
            await reader.ReadLineAsync(); // status request
            await state.Task.WaitAsync(TimeSpan.FromSeconds(10)); // stay connected like the real service
        });

        await client.StartAsync(CancellationToken.None);

        Assert.Equal(MonitoringState.Active, await state.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await serve;
    }

    [Fact]
    public async Task WritesNoByteOrderMarks()
    {
        var fake = new FakeService();
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, fake.PipeName);
        var lines = new List<string?>();

        var serve = fake.ServeOnceAsync(async (pipe, reader) =>
        {
            var writer = IpcLineCodec.CreateWriter(pipe);
            await writer.WriteLineAsync(IpcLineCodec.Serialize(new IpcEnvelope
            {
                Type = IpcMessageTypes.NonceChallenge,
                Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload("n"))
            }));
            lines.Add(await reader.ReadLineAsync()); // NonceResponse
            lines.Add(await reader.ReadLineAsync()); // StatusRequest
        });

        await client.StartAsync(CancellationToken.None);
        await serve.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(lines, l => Assert.False(string.IsNullOrEmpty(l) || l[0] == '﻿'));
        Assert.True(IpcLineCodec.TryParse(lines[1], out var request));
        Assert.Equal(IpcMessageTypes.StatusRequest, request!.Type);
    }
}

public class NamedPipeClientReconnectTests
{
    [Fact]
    public async Task ReconnectsAfterServiceRestart()
    {
        // ISS-013 regression: after a session ended the client never reconnected.
        var fake = new FakeService();
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, fake.PipeName);
        var states = new System.Collections.Concurrent.BlockingCollection<MonitoringState>();
        var disconnects = 0;
        client.OnStateReceived += states.Add;
        client.OnDisconnected += () => Interlocked.Increment(ref disconnects);

        var firstSession = fake.ServeStatusSessionAsync(MonitoringState.Stopped, holdOpen: TimeSpan.FromMilliseconds(300));
        await client.StartAsync(CancellationToken.None);
        Assert.True(states.TryTake(out var first, TimeSpan.FromSeconds(10)));
        Assert.Equal(MonitoringState.Stopped, first);
        await firstSession; // "service restart": the session ends

        var secondSession = fake.ServeStatusSessionAsync(MonitoringState.Active, holdOpen: TimeSpan.FromSeconds(2));
        Assert.True(states.TryTake(out var second, TimeSpan.FromSeconds(15)), "client did not reconnect");
        Assert.Equal(MonitoringState.Active, second);
        Assert.True(disconnects >= 1);
        await secondSession;
    }
}

/// <summary>Minimal stand-in for the Agent Service's pipe endpoint.</summary>
internal sealed class FakeService
{
    public string PipeName { get; } = "onevo-tray-test-" + Guid.NewGuid().ToString("N");

    public async Task ServeOnceAsync(Func<NamedPipeServerStream, StreamReader, Task> session)
    {
        await using var pipe = NewServer();
        await pipe.WaitForConnectionAsync();
        using var reader = IpcLineCodec.CreateReader(pipe);
        await session(pipe, reader);
    }

    /// <summary>Handshake, answer one StatusRequest with <paramref name="state"/>, keep the pipe open, then close.</summary>
    public Task ServeStatusSessionAsync(MonitoringState state, TimeSpan holdOpen) => ServeOnceAsync(async (pipe, reader) =>
    {
        var writer = IpcLineCodec.CreateWriter(pipe);
        await writer.WriteLineAsync(IpcLineCodec.Serialize(new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceChallenge,
            Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload("n"))
        }));
        await reader.ReadLineAsync(); // NonceResponse
        await reader.ReadLineAsync(); // StatusRequest
        await writer.WriteLineAsync(IpcLineCodec.Serialize(new IpcEnvelope
        {
            Type = IpcMessageTypes.StatusResponse,
            Payload = JsonSerializer.SerializeToElement(new StatusResponsePayload(state, DateTimeOffset.UtcNow))
        }));
        await Task.Delay(holdOpen);
    });

    public NamedPipeServerStream NewServer() => new(PipeName, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}
