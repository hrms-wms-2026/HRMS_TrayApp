namespace ONEVO.Agent.TrayApp.Tests;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

public class CollectorCoordinatorTests
{
    [Fact]
    public async Task RapidStateChanges_NeverOverlapStartStop_AndEndInLatestState()
    {
        // ISS-015 regression: each event started an unserialized reconcile, so Start and Stop
        // ran concurrently and the collector could be left running during a break.
        var fake = new FakeService();
        var collector = new RecordingCollector();
        await using var client = new NamedPipeClient(NullLogger<NamedPipeClient>.Instance, fake.PipeName);
        await using var coordinator = new CollectorCoordinator(
            NullLogger<CollectorCoordinator>.Instance, [collector], client);

        var sequence = new[]
        {
            MonitoringState.Active, MonitoringState.Paused, MonitoringState.Active,
            MonitoringState.Paused, MonitoringState.Active, MonitoringState.Paused
        };
        var serve = fake.ServeOnceAsync(async (pipe, reader) =>
        {
            var writer = IpcLineCodec.CreateWriter(pipe);
            await writer.WriteLineAsync(Envelope(IpcMessageTypes.NonceChallenge, new NonceChallengePayload("n")));
            await reader.ReadLineAsync(); // NonceResponse
            await reader.ReadLineAsync(); // StatusRequest
            await writer.WriteLineAsync(Envelope(IpcMessageTypes.PolicyPush, new PolicyPushPayload
            {
                Policy = new AgentPolicy { Version = "test", ActivitySignalEnabled = true }
            }));
            foreach (var state in sequence)
                await writer.WriteLineAsync(Envelope(IpcMessageTypes.StatusResponse,
                    new StatusResponsePayload(state, DateTimeOffset.UtcNow)));
            await Task.Delay(TimeSpan.FromSeconds(3)); // let reconciles drain while connected
        });

        await client.StartAsync(CancellationToken.None);
        await serve.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(1, collector.MaxConcurrentCalls);
        Assert.False(collector.Running);
        Assert.True(collector.Starts >= 1);
    }

    private static string Envelope<T>(string type, T payload) => IpcLineCodec.Serialize(new IpcEnvelope
    {
        Type = type,
        Payload = JsonSerializer.SerializeToElement(payload)
    });

    private sealed class RecordingCollector : IAgentCollector
    {
        private int _inFlight;
        public int MaxConcurrentCalls;
        public int Starts;
        public bool Running;

        public string Name => "Recording";

        public Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken) =>
            Track(() => { Running = true; Starts++; });

        public Task StopAsync(CancellationToken cancellationToken) =>
            Track(() => Running = false);

        private async Task Track(Action apply)
        {
            var now = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref MaxConcurrentCalls, now);
            await Task.Delay(50); // collectors do real work (hook install, final flush)
            apply();
            Interlocked.Decrement(ref _inFlight);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }
    }
}
