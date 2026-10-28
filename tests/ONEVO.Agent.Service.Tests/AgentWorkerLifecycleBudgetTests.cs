using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// AUD-008: a clock action whose backend call never answers must come back to the tray as a
/// definite TIMED_OUT within the lifecycle budget — not hang past the tray's wait while the action
/// may still commit — and must leave local state unchanged.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerLifecycleBudgetTests : IDisposable
{
    private readonly CredentialStore _credentials = TestStores.NewCredentialStore();

    public void Dispose() => _credentials.ClearDeviceJwt();

    [Theory]
    [InlineData(LifecycleAction.ClockIn, MonitoringState.Stopped)]
    [InlineData(LifecycleAction.StartBreak, MonitoringState.Active)]
    [InlineData(LifecycleAction.ClockOut, MonitoringState.Active)]
    public async Task UnresponsiveBackend_AnswersTimedOutWithinTheBudget(LifecycleAction action, MonitoringState startState)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        if (startState == MonitoringState.Active)
            stateMachine.TryTransition(MonitoringState.Active, out _);

        _credentials.StoreDeviceJwt("device-jwt");
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "p", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!,
            stateMachine,
            policyCache,
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(new StubHttpClientFactory(new HangingHandler()), NullLogger<OnevoApiClient>.Instance),
            _credentials,
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!,
            null!,
            null!,
            new PendingLegalChallengeStore())
        {
            LifecycleBudget = TimeSpan.FromMilliseconds(300)
        };
        worker.ApplyEnrollmentGates();

        LifecycleResultPayload? result = null;
        var stopwatch = Stopwatch.StartNew();
        await worker.HandleLifecycleCommandAsync(
            new IpcEnvelope
            {
                Type = IpcMessageTypes.LifecycleCommand,
                CorrelationId = "c1",
                Payload = JsonSerializer.SerializeToElement(new LifecycleCommandPayload(action))
            },
            reply =>
            {
                if (reply.Type == IpcMessageTypes.LifecycleResult)
                    result = reply.Payload!.Value.Deserialize<LifecycleResultPayload>();
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("TIMED_OUT", result.ErrorCode);
        Assert.Equal(startState, stateMachine.CurrentState);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };
    }
}
