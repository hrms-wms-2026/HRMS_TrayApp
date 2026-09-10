namespace ONEVO.Agent.Service.Tests;

using System.Net;
using System.Net.Http.Json;
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

// See CredentialStoreFileCollection's doc comment: CredentialStore/DeviceIdentityStore read/write
// real shared files, so every test class touching them must serialize against the others.
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerHandleWorkLocationConfirmTests : IDisposable
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
            null!  // EvidenceSpoolStore — not touched by this handler
        );
        worker.ApplyEnrollmentGates();
        return worker;
    }

    [Fact]
    public async Task HandleWorkLocationConfirmAsync_ValidPayload_CallsApiClientAndReplies()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var worker = BuildWorker(handler);

        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.WorkLocationConfirm,
            CorrelationId = "corr-1",
            Payload = JsonSerializer.SerializeToElement(
                new WorkLocationConfirmPayload("home", 6.9271, 79.8612, 15))
        };

        WorkLocationConfirmResultPayload? result = null;
        IpcEnvelope? replyEnvelope = null;
        await worker.HandleWorkLocationConfirmAsync(envelope, reply =>
        {
            replyEnvelope = reply;
            result = reply.Payload!.Value.Deserialize<WorkLocationConfirmResultPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(captured);
        Assert.Equal(AgentApiRoutes.WorkLocationConfirmSubmit, captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"location_type\":\"home\"", capturedBody);
        Assert.Contains("\"latitude\":6.9271", capturedBody);
        Assert.Contains("\"longitude\":79.8612", capturedBody);
        Assert.Contains("\"accuracy_meters\":15", capturedBody);

        Assert.NotNull(replyEnvelope);
        Assert.Equal(IpcMessageTypes.WorkLocationConfirmResult, replyEnvelope!.Type);
        Assert.Equal(envelope.CorrelationId, replyEnvelope.CorrelationId);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task HandleWorkLocationConfirmAsync_NoDeviceJwt_RepliesUnenrolled()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend without a device JWT"));
        var worker = BuildWorker(handler, storeDeviceJwt: false);

        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.WorkLocationConfirm,
            CorrelationId = "corr-2",
            Payload = JsonSerializer.SerializeToElement(
                new WorkLocationConfirmPayload("home", 6.9271, 79.8612, 15))
        };

        WorkLocationConfirmResultPayload? result = null;
        IpcEnvelope? replyEnvelope = null;
        await worker.HandleWorkLocationConfirmAsync(envelope, reply =>
        {
            replyEnvelope = reply;
            result = reply.Payload!.Value.Deserialize<WorkLocationConfirmResultPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(replyEnvelope);
        Assert.Equal(IpcMessageTypes.WorkLocationConfirmResult, replyEnvelope!.Type);
        Assert.Equal(envelope.CorrelationId, replyEnvelope.CorrelationId);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("UNENROLLED", result.ErrorCode);
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
