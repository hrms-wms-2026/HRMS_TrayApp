using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.IPC;

/// <summary>
/// AUD-008 / UAT-17: a request waiting on a slow backend call must not hold back the messages
/// behind it on the same connection, while ordered messages keep their arrival order.
/// Runs over a real pipe pair (<see cref="TestPipeConnection"/>).
/// </summary>
public sealed class NamedPipeServerDispatchTests
{
    [Fact]
    public async Task SlowIndependentRequest_DoesNotHoldBackALaterLifecycleCommand()
    {
        var backend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await ConnectAsync(async (envelope, reply) =>
        {
            if (envelope.Type == IpcMessageTypes.DailyTasksRequest)
                await backend.Task; // a backend call that has not answered yet
            await reply(ReplyTo(envelope));
        });

        // Before the fix the lifecycle command was not even read until the daily-tasks call
        // returned, so the first read (or the second send) timed out.
        var firstReply = harness.Client.ReadEnvelopeAsync(harness.Cts.Token);
        await SendAsync(harness, Request(IpcMessageTypes.DailyTasksRequest, "slow"));
        await SendAsync(harness, Request(IpcMessageTypes.LifecycleCommand, "clock-in"));
        Assert.Equal("clock-in", (await firstReply).CorrelationId);

        var secondReply = harness.Client.ReadEnvelopeAsync(harness.Cts.Token);
        backend.SetResult();
        Assert.Equal("slow", (await secondReply).CorrelationId);
    }

    [Fact]
    public async Task OrderedMessages_AreHandledOneAtATime_InArrivalOrder()
    {
        var handled = new List<string>();
        var inHandler = 0;
        var overlapped = false;
        await using var harness = await ConnectAsync(async (envelope, reply) =>
        {
            if (Interlocked.Increment(ref inHandler) > 1) overlapped = true;
            lock (handled) handled.Add(envelope.CorrelationId);
            // The first one is slow: if ordered messages ran concurrently, the others would finish first.
            if (envelope.CorrelationId == "1") await Task.Delay(200);
            Interlocked.Decrement(ref inHandler);
            await reply(ReplyTo(envelope));
        });

        var replies = ReadCorrelationIdsAsync(harness, 3);
        await SendAsync(harness, Request(IpcMessageTypes.LifecycleCommand, "1"));
        await SendAsync(harness, Request(IpcMessageTypes.CollectionRecordSubmit, "2"));
        await SendAsync(harness, Request(IpcMessageTypes.LifecycleCommand, "3"));

        var expected = new[] { "1", "2", "3" };
        Assert.Equal(expected, await replies);
        lock (handled) Assert.Equal(expected, handled);
        Assert.False(overlapped);
    }

    [Fact]
    public async Task IndependentRequests_InFlightPerConnection_AreBounded()
    {
        var backend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maxInFlight = 0;
        await using var harness = await ConnectAsync(async (envelope, reply) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await backend.Task;
            Interlocked.Decrement(ref inFlight);
            await reply(ReplyTo(envelope));
        });

        var limit = IpcDispatch.MaxConcurrentRequestsPerConnection;
        var total = limit + 2;
        // Once every slot is busy the server stops reading, so the last sends only complete
        // after a slot frees up: send from a background task.
        var sends = Task.Run(async () =>
        {
            for (var i = 0; i < total; i++)
                await harness.Client.SendAsync(Request(IpcMessageTypes.DailyTasksRequest, $"r{i}"));
        });

        await WaitUntilAsync(() => Volatile.Read(ref inFlight) == limit);
        await Task.Delay(200); // give any request beyond the limit the chance to (wrongly) start
        Assert.Equal(limit, Volatile.Read(ref maxInFlight));

        var replies = ReadCorrelationIdsAsync(harness, total);
        backend.SetResult();
        await sends.WaitAsync(TestPipeConnection.IoTimeout);

        Assert.Equal(total, (await replies).Distinct().Count());
        Assert.Equal(limit, Volatile.Read(ref maxInFlight));
    }

    [Fact]
    public async Task IndependentRequest_WhoseHandlerThrows_LeavesTheConnectionUp()
    {
        await using var harness = await ConnectAsync(async (envelope, reply) =>
        {
            if (envelope.Type == IpcMessageTypes.UpdateCheckRequest)
                throw new InvalidOperationException("backend call failed");
            await reply(ReplyTo(envelope));
        });

        var answer = harness.Client.ReadEnvelopeAsync(harness.Cts.Token);
        await SendAsync(harness, Request(IpcMessageTypes.UpdateCheckRequest, "fails"));
        await SendAsync(harness, Request(IpcMessageTypes.StatusRequest, "status"));

        Assert.Equal("status", (await answer).CorrelationId);
    }

    [Theory]
    [InlineData(IpcMessageTypes.DailyTasksRequest)]
    [InlineData(IpcMessageTypes.LocationChangeSubmit)]
    [InlineData(IpcMessageTypes.LocationChangePendingCheck)]
    [InlineData(IpcMessageTypes.LocationChangeRespond)]
    [InlineData(IpcMessageTypes.WorkLocationConfirm)]
    [InlineData(IpcMessageTypes.FaceReferenceStatus)]
    [InlineData(IpcMessageTypes.PermissionConsentStatus)]
    [InlineData(IpcMessageTypes.PermissionConsentSubmit)]
    [InlineData(IpcMessageTypes.GenericNotificationAccepted)]
    [InlineData(IpcMessageTypes.UpdateCheckRequest)]
    public void IndependentRequests_RunConcurrently(string type) =>
        Assert.True(IpcDispatch.RunsConcurrently(type));

    // Messages that change Service state or depend on the order they arrive in.
    [Theory]
    [InlineData(IpcMessageTypes.StatusRequest)]
    [InlineData(IpcMessageTypes.LifecycleCommand)]
    [InlineData(IpcMessageTypes.CollectionRecordSubmit)]
    [InlineData(IpcMessageTypes.ActivationCodeSubmit)]
    [InlineData(IpcMessageTypes.DevicePairingStart)]
    [InlineData(IpcMessageTypes.LogoutRequest)]
    [InlineData(IpcMessageTypes.BiometricEnrollmentStart)]
    [InlineData(IpcMessageTypes.FacePhotoValidate)]
    [InlineData(IpcMessageTypes.FaceEnrollCommit)]
    [InlineData(IpcMessageTypes.LegalAcceptanceSubmit)]
    [InlineData(IpcMessageTypes.EvidenceTransferChunk)]
    [InlineData("SomeFutureMessage")]
    public void StatefulOrUnknownMessages_StayOnTheReadLoop(string type) =>
        Assert.False(IpcDispatch.RunsConcurrently(type));

    private static Task SendAsync(Harness harness, IpcEnvelope envelope) =>
        harness.Client.SendAsync(envelope).WaitAsync(TestPipeConnection.IoTimeout);

    private static async Task<List<string>> ReadCorrelationIdsAsync(Harness harness, int count)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
            ids.Add((await harness.Client.ReadEnvelopeAsync(harness.Cts.Token)).CorrelationId);
        return ids;
    }

    private static IpcEnvelope Request(string type, string correlationId) =>
        new() { Type = type, CorrelationId = correlationId };

    private static IpcEnvelope ReplyTo(IpcEnvelope request) =>
        new() { Type = request.Type + "Reply", CorrelationId = request.CorrelationId };

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestPipeConnection.IoTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not reached");
            await Task.Delay(10);
        }
    }

    private static async Task<Harness> ConnectAsync(Func<IpcEnvelope, Func<IpcEnvelope, Task>, Task> handler)
    {
        var server = new NamedPipeServer(NullLogger<NamedPipeServer>.Instance, new NamedPipeAuthenticator(), TrustAllClients.Instance);
        server.MessageReceived += handler;
        var cts = new CancellationTokenSource();
        var client = await TestPipeConnection.AttachClientAsync(server, cts.Token);
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
