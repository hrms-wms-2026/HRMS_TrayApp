namespace ONEVO.Agent.Service.Tests;

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

[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerTests : IDisposable
{
    public void Dispose() => new CredentialStore().ClearDeviceJwt();

    private static AgentWorker CreateSut(
        PolicyCache? policyCache = null,
        OnevoApiClient? apiClient = null,
        bool allowLocalLifecycleWithoutFullGates = false,
        bool storeDeviceJwt = true)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        var credentials = new CredentialStore();
        if (storeDeviceJwt)
            credentials.StoreDeviceJwt("test-device-jwt");
        else
            credentials.ClearDeviceJwt();

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var defaultApiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by lifecycle commands
            stateMachine,
            policyCache ?? new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions { AllowLocalLifecycleWithoutFullGates = allowLocalLifecycleWithoutFullGates }),
            apiClient ?? defaultApiClient,
            credentials,
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!, // EnrollmentCoordinator — not touched by lifecycle commands
            null!, // InactivityEvidenceHandler — not touched by lifecycle commands
            null!  // EvidenceSpoolStore — not touched by lifecycle commands
        );
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static async Task<LifecycleResultPayload> SendLifecycleAsync(AgentWorker worker, LifecycleAction action)
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
    public async Task ExecuteClockIn_TrayClockInDisabled_ReturnsErrorWithoutCallingBackend()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = false, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend when TrayClockInEnabled is false"));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.False(result.Success);
        Assert.Equal("TRAY_CLOCK_IN_NOT_ALLOWED", result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteClockIn_TrayClockInEnabledAndBackendSucceeds_TransitionsToActive()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, allowLocalLifecycleWithoutFullGates: true);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
    }

    [Fact]
    public async Task ExecuteClockIn_BackendForbidden_ReturnsTrayClockInNotAllowedWithoutLocalTransition()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, allowLocalLifecycleWithoutFullGates: true);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.False(result.Success);
        Assert.Equal("TRAY_CLOCK_IN_NOT_ALLOWED", result.ErrorCode);
        Assert.Equal(MonitoringState.Stopped, result.State);
    }

    [Fact]
    public async Task ExecuteClockIn_NoDeviceJwt_ReturnsUnenrolledWithoutCallingBackend()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend without a device JWT"));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, storeDeviceJwt: false);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.False(result.Success);
        Assert.Equal("UNENROLLED", result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteClockOut_TrayClockInDisabled_ReturnsErrorWithoutCallingBackend()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var okHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var okApiClient = new OnevoApiClient(new StubHttpClientFactory(okHandler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: okApiClient, allowLocalLifecycleWithoutFullGates: true);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        // Flip the policy off mid-session, mirroring a poll picking up a work-mode change.
        policyCache.Set(new AgentPolicy { Version = "v2", TrayClockInEnabled = false, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockOut);

        Assert.False(result.Success);
        Assert.Equal("TRAY_CLOCK_IN_NOT_ALLOWED", result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
    }

    [Fact]
    public async Task ExecuteClockOut_TrayClockInEnabledAndBackendSucceeds_TransitionsToStoppedWithSuccessMessage()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, allowLocalLifecycleWithoutFullGates: true);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockOut);

        // Regression guard: Success/ErrorCode/Message must not be positionally swapped —
        // a successful clock-out must carry a null ErrorCode and a human-readable Message.
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal("Clocked out. Workday completed.", result.Message);
        Assert.Equal(MonitoringState.Stopped, result.State);
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
}
