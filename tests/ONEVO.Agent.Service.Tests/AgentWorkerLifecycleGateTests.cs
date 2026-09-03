using System.Net;
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
/// Regression coverage for the release-hardening requirement that attendance clock-in/break
/// transitions must never depend on monitoring being enabled or reachable. LifecycleGate.CanActivate
/// aggregates nine conditions including PolicyAllowsCollection, and AgentWorker.ExecuteClockIn/
/// ExecuteEndBreak refuse the transition ("GATES_CLOSED") when CanActivate is false — so if
/// PolicyAllowsCollection were ever wired to the real, possibly fail-closed monitoring policy
/// (PolicyCache.Current), a monitoring outage or a monitoring-disabled tenant would also block
/// clock-in. ApplyEnrollmentGates deliberately keeps PolicyAllowsCollection hardcoded true
/// regardless of PolicyCache's contents (see its own doc comment, §23 gap) specifically to avoid
/// that coupling. These tests prove the decoupling holds even in the worst case: a PolicyCache that
/// was never successfully populated (Current resolves to the fail-closed CreateDefault(), i.e. a
/// simulated total monitoring/server-policy failure).
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerLifecycleGateTests : IDisposable
{
    public void Dispose() => new CredentialStore().ClearDeviceJwt();


    // TrayClockInEnabled is a distinct, orthogonal gate from the monitoring-toggle capabilities
    // this class is about (PolicyAllowsCollection etc.) — every fixture here sets it true so
    // these tests keep proving what they're meant to prove (monitoring-policy unavailability
    // does not block clock-in/out) rather than tripping the unrelated tray-eligibility gate.
    private static AgentWorker BuildStoppedWorkerWithUnavailablePolicy(out PresenceSession presence, out LifecycleGate gate)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        presence = new PresenceSession();
        gate = new LifecycleGate();

        // Every monitoring capability flag stays false/"none" (CreateDefault()'s worst case) —
        // only TrayClockInEnabled is overridden true, so the monitoring-unavailable scenario this
        // class tests for is otherwise unchanged.
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy
        {
            Version = "test",
            TrayClockInEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });

        var credentials = new CredentialStore();
        credentials.StoreDeviceJwt("test-device-jwt");

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by lifecycle commands
            stateMachine,
            policyCache,
            ActivityRecordBuffer.CreateInMemory(),
            presence,
            gate,
            Options.Create(new AgentOptions()),
            apiClient,
            credentials,
            new DeviceIdentityStore(),
            null!, // EnrollmentCoordinator — not touched by lifecycle commands
            null!, // InactivityEvidenceHandler — not touched by lifecycle commands
            null!  // EvidenceSpoolStore — not touched by lifecycle commands
        );

        // Mirrors what a real successful enrollment/session-resume applies before any lifecycle
        // command can be sent. PolicyAllowsCollection is set true here, independent of policyCache.
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static async Task<LifecycleResultPayload> SendLifecycleAsync(
        AgentWorker worker, LifecycleAction action)
    {
        LifecycleResultPayload? result = null;
        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.LifecycleCommand,
            Payload = JsonSerializer.SerializeToElement(new LifecycleCommandPayload(action))
        };

        await worker.HandleLifecycleCommandAsync(envelope, reply =>
        {
            if (reply.Type == IpcMessageTypes.LifecycleResult)
                result = reply.Payload!.Value.Deserialize<LifecycleResultPayload>();
            return Task.CompletedTask;
        });

        return result!;
    }

    [Fact]
    public async Task ClockIn_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out var gate);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
        Assert.True(gate.CanActivate, "the gate itself must be satisfied — PolicyAllowsCollection is decoupled from PolicyCache, not merely bypassed");
    }

    [Fact]
    public async Task EndBreak_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out _);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);
        await SendLifecycleAsync(worker, LifecycleAction.StartBreak);

        var result = await SendLifecycleAsync(worker, LifecycleAction.EndBreak);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
    }

    [Fact]
    public async Task ClockOut_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out _);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockOut);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Stopped, result.State);
    }
}
