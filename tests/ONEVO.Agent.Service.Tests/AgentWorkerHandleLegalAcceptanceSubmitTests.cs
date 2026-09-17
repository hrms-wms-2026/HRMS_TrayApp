namespace ONEVO.Agent.Service.Tests;

using System.Linq;
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

// See CredentialStoreFileCollection's doc comment: CredentialStore/DeviceIdentityStore read/write
// real shared files, so every test class touching them must serialize against the others.
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerHandleLegalAcceptanceSubmitTests : IDisposable
{
    public void Dispose() => new CredentialStore().ClearDeviceJwt();

    private static AgentWorker BuildWorker(HttpMessageHandler handler, PendingLegalChallengeStore challengeStore)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

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
            new CredentialStore(),
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!, // EnrollmentCoordinator — not touched by this handler
            null!, // InactivityEvidenceHandler — not touched by this handler
            null!, // EvidenceSpoolStore — not touched by this handler
            challengeStore
        );
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static IpcEnvelope BuildSubmitEnvelope(string correlationId = "corr-1") => new()
    {
        Type = IpcMessageTypes.LegalAcceptanceSubmit,
        CorrelationId = correlationId,
        Payload = JsonSerializer.SerializeToElement(
            new LegalAcceptanceSubmitPayload(new[] { new LegalAcceptanceItemPayload("privacy_policy", "2.0") }))
    };

    [Fact]
    public async Task HandleLegalAcceptanceSubmitAsync_WhenChallengePending_SendsChallengeAndAcceptedDocuments()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var challengeStore = new PendingLegalChallengeStore();
        challengeStore.Set("raw-challenge", "raw-csrf");
        var worker = BuildWorker(handler, challengeStore);

        LegalAcceptanceResultPayload? result = null;
        IpcEnvelope? replyEnvelope = null;
        await worker.HandleLegalAcceptanceSubmitAsync(BuildSubmitEnvelope(), reply =>
        {
            replyEnvelope = reply;
            result = reply.Payload!.Value.Deserialize<LegalAcceptanceResultPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(captured);
        Assert.Equal(AgentApiRoutes.LegalAcceptanceComplete, captured!.RequestUri!.AbsolutePath);
        Assert.Equal("onevo_legal_pending=raw-challenge", captured.Headers.GetValues("Cookie").Single());
        Assert.Equal("raw-csrf", captured.Headers.GetValues("X-CSRF-Token").Single());

        Assert.NotNull(replyEnvelope);
        Assert.Equal(IpcMessageTypes.LegalAcceptanceResult, replyEnvelope!.Type);
        Assert.Equal("corr-1", replyEnvelope.CorrelationId);
        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task HandleLegalAcceptanceSubmitAsync_WhenNoPendingChallenge_RepliesFailureWithoutCallingBackend()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend without a pending challenge"));
        var worker = BuildWorker(handler, new PendingLegalChallengeStore());

        LegalAcceptanceResultPayload? result = null;
        await worker.HandleLegalAcceptanceSubmitAsync(BuildSubmitEnvelope(), reply =>
        {
            result = reply.Payload!.Value.Deserialize<LegalAcceptanceResultPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public async Task HandleLegalAcceptanceSubmitAsync_WhenBackendSucceeds_ClearsPendingChallenge()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var challengeStore = new PendingLegalChallengeStore();
        challengeStore.Set("raw-challenge", "raw-csrf");
        var worker = BuildWorker(handler, challengeStore);

        await worker.HandleLegalAcceptanceSubmitAsync(BuildSubmitEnvelope(), _ => Task.CompletedTask);

        Assert.Null(challengeStore.Current);
    }

    [Fact]
    public async Task HandleLegalAcceptanceSubmitAsync_WhenBackendRejects_LeavesPendingChallengeIntact()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var challengeStore = new PendingLegalChallengeStore();
        challengeStore.Set("raw-challenge", "raw-csrf");
        var worker = BuildWorker(handler, challengeStore);

        LegalAcceptanceResultPayload? result = null;
        await worker.HandleLegalAcceptanceSubmitAsync(BuildSubmitEnvelope(), reply =>
        {
            result = reply.Payload!.Value.Deserialize<LegalAcceptanceResultPayload>();
            return Task.CompletedTask;
        });

        Assert.False(result!.Success);
        Assert.NotNull(challengeStore.Current);
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
