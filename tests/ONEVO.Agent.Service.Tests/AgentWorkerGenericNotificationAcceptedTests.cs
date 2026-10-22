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
public class AgentWorkerGenericNotificationAcceptedTests : IDisposable
{
    public void Dispose() => new CredentialStore().ClearDeviceJwt();

    private static AgentWorker BuildWorker(HttpMessageHandler handler, bool storeDeviceJwt = true)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        var credentials = new CredentialStore();
        if (storeDeviceJwt)
            credentials.StoreDeviceJwt("test-device-jwt");
        else
            credentials.ClearDeviceJwt();

        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by this handler
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            apiClient,
            credentials,
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!, // EnrollmentCoordinator — not touched by this handler
            null!, // InactivityEvidenceHandler — not touched by this handler
            null!, // EvidenceSpoolStore — not touched by this handler
            new PendingLegalChallengeStore());
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static IpcEnvelope Accepted(Guid deliveryId) => new()
    {
        Type = IpcMessageTypes.GenericNotificationAccepted,
        Payload = JsonSerializer.SerializeToElement(new GenericNotificationAcceptedPayload(deliveryId))
    };

    [Fact]
    public async Task TrayAccepted_AcknowledgesThatDeliveryWithTheDeviceJwt()
    {
        var deliveryId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var worker = BuildWorker(new StubHandler(r => { captured = r; return new HttpResponseMessage(HttpStatusCode.NoContent); }));

        await worker.HandleGenericNotificationAcceptedAsync(Accepted(deliveryId));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(string.Format(AgentApiRoutes.AckTrayDelivery, deliveryId), captured.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("test-device-jwt", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task NoDeviceJwt_DoesNotCallTheBackend()
    {
        var worker = BuildWorker(new StubHandler(_ => throw new InvalidOperationException("must not call backend")), storeDeviceJwt: false);

        await worker.HandleGenericNotificationAcceptedAsync(Accepted(Guid.NewGuid()));
    }

    [Fact]
    public async Task MalformedPayload_IsIgnored()
    {
        var worker = BuildWorker(new StubHandler(_ => throw new InvalidOperationException("must not call backend")));

        await worker.HandleGenericNotificationAcceptedAsync(new IpcEnvelope { Type = IpcMessageTypes.GenericNotificationAccepted });
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
