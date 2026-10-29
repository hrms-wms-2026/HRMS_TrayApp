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
/// AUD-035: the backend answers a break POST and pushes SignalR AttendanceChanged at the same time.
/// When the push-triggered attendance poll moves local state first, the action itself succeeded and
/// must be reported as success — not "Cannot start break." / "Cannot end break." (seen live 2026-10-29).
/// The stub backend applies that reconciliation while the POST is in flight, so the race is deterministic.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class AgentWorkerBreakRaceTests : IDisposable
{
    private readonly CredentialStore _credentials = TestStores.NewCredentialStore();
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), $"onevo-break-race-{Guid.NewGuid():N}");
    private readonly AgentStateMachine _stateMachine = new();
    private readonly PresenceSession _presence = new();
    private AgentWorker _worker = null!;
    private Action<AgentWorker>? _whileBreakStartInFlight;
    private Action<AgentWorker>? _whileBreakEndInFlight;
    private Action<AgentWorker>? _whileClockOutInFlight;

    public AgentWorkerBreakRaceTests()
    {
        _credentials.StoreDeviceJwt("device-jwt");
        var identity = new DeviceIdentityStore(_identityDir);
        identity.Save(new DeviceIdentity { DeviceId = "d", AgentId = "a", TenantId = "t", DeviceFingerprint = "fp" });
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "p", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });

        var handler = new StubHandler(request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case AgentApiRoutes.TrayBreakStart: _whileBreakStartInFlight?.Invoke(_worker); break;
                case AgentApiRoutes.TrayBreakEnd: _whileBreakEndInFlight?.Invoke(_worker); break;
                case AgentApiRoutes.TrayClockOut: _whileClockOutInFlight?.Invoke(_worker); break;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        _worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — replies go through the callback
            _stateMachine,
            policyCache,
            ActivityRecordBuffer.CreateInMemory(),
            _presence,
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance),
            _credentials,
            identity,
            null!,
            null!,
            null!,
            new PendingLegalChallengeStore());
        _worker.ApplyEnrollmentGates();
        _stateMachine.TryTransition(MonitoringState.Stopped, out _);
    }

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        if (Directory.Exists(_identityDir))
            Directory.Delete(_identityDir, recursive: true);
    }

    [Fact]
    public async Task StartBreak_PollAppliesBreakFirst_ReportsSuccessAndCountsOneBreak()
    {
        Assert.True((await SendAsync(LifecycleAction.ClockIn)).Success);
        _whileBreakStartInFlight = w => w.ApplyPresenceBreakStarted(DateTimeOffset.UtcNow);

        var result = await SendAsync(LifecycleAction.StartBreak);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal(MonitoringState.Paused, result.State);
        Assert.Equal(MonitoringState.Paused, _stateMachine.CurrentState);
        var snap = _presence.Snapshot(DateTimeOffset.UtcNow);
        Assert.True(snap.IsOnBreak);
        Assert.Equal(1, snap.BreakSessionCount);
    }

    [Fact]
    public async Task EndBreak_PollAppliesBreakEndFirst_ReportsSuccess()
    {
        Assert.True((await SendAsync(LifecycleAction.ClockIn)).Success);
        Assert.True((await SendAsync(LifecycleAction.StartBreak)).Success);
        _whileBreakEndInFlight = w => w.ApplyPresenceBreakEnded(DateTimeOffset.UtcNow);

        var result = await SendAsync(LifecycleAction.EndBreak);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal(MonitoringState.Active, result.State);
        Assert.Equal(MonitoringState.Active, _stateMachine.CurrentState);
        var snap = _presence.Snapshot(DateTimeOffset.UtcNow);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(1, snap.BreakSessionCount);
    }

    [Fact]
    public async Task ClockOut_PollAppliesClockOutFirst_ReportsSuccess()
    {
        // Already idempotent before AUD-035; pinned so it stays that way.
        Assert.True((await SendAsync(LifecycleAction.ClockIn)).Success);
        _whileClockOutInFlight = w => w.ApplyPresenceStopped(DateTimeOffset.UtcNow);

        var result = await SendAsync(LifecycleAction.ClockOut);

        Assert.True(result.Success, $"{result.ErrorCode}: {result.Message}");
        Assert.Equal(MonitoringState.Stopped, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task StartBreak_WithoutRace_StillStartsOneBreak()
    {
        Assert.True((await SendAsync(LifecycleAction.ClockIn)).Success);

        var result = await SendAsync(LifecycleAction.StartBreak);

        Assert.True(result.Success);
        Assert.Equal(MonitoringState.Paused, _stateMachine.CurrentState);
        Assert.Equal(1, _presence.Snapshot(DateTimeOffset.UtcNow).BreakSessionCount);
    }

    private async Task<LifecycleResultPayload> SendAsync(LifecycleAction action)
    {
        LifecycleResultPayload? result = null;
        await _worker.HandleLifecycleCommandAsync(
            new IpcEnvelope
            {
                Type = IpcMessageTypes.LifecycleCommand,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Payload = JsonSerializer.SerializeToElement(new LifecycleCommandPayload(action))
            },
            reply =>
            {
                if (reply.Type == IpcMessageTypes.LifecycleResult)
                    result = reply.Payload!.Value.Deserialize<LifecycleResultPayload>();
                return Task.CompletedTask;
            });
        return Assert.IsType<LifecycleResultPayload>(result);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
