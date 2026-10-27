namespace ONEVO.Agent.Service.Tests;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

/// <summary>
/// Silent session resume on Service start: the backend being unreachable at boot must not
/// cost the employee a new activation code — only a 401 from refresh clears credentials.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class AgentWorkerSessionResumeTests : IDisposable
{
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), "onevo-resume-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CredentialStore _credentials = new();
    private readonly DeviceIdentityStore _identityStore;
    private readonly AgentStateMachine _stateMachine = new();

    public AgentWorkerSessionResumeTests()
    {
        _identityStore = new DeviceIdentityStore(_identityDir);
        _identityStore.Save(new DeviceIdentity
        {
            DeviceId = "device-1",
            AgentId = "agent-1",
            TenantId = "tenant-1",
            DeviceFingerprint = "fp-1",
        });
        _credentials.StoreDeviceJwt("stale-jwt");
        _credentials.StoreRefreshToken("stored-refresh");
    }

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
        if (Directory.Exists(_identityDir))
            Directory.Delete(_identityDir, recursive: true);
    }

    [Fact]
    public async Task TryResumeSession_BackendUnavailable_KeepsCredentialsAndEnrollment()
    {
        var worker = CreateSut(HttpStatusCode.ServiceUnavailable);

        await worker.TryResumeSessionAsync(CancellationToken.None);

        Assert.Equal(MonitoringState.Stopped, _stateMachine.CurrentState);
        Assert.Equal("stale-jwt", _credentials.ReadDeviceJwt());
        Assert.Equal("stored-refresh", _credentials.ReadRefreshToken());
        Assert.NotNull(_identityStore.Load());
    }

    [Fact]
    public async Task TryResumeSession_RefreshRejected_ClearsCredentials()
    {
        var worker = CreateSut(HttpStatusCode.Unauthorized);

        await worker.TryResumeSessionAsync(CancellationToken.None);

        Assert.Equal(MonitoringState.Unenrolled, _stateMachine.CurrentState);
        Assert.Null(_credentials.ReadRefreshToken());
    }

    private AgentWorker CreateSut(HttpStatusCode refreshStatus)
    {
        var apiClient = new OnevoApiClient(
            new StubHttpClientFactory(new StubHandler(_ => new HttpResponseMessage(refreshStatus))),
            NullLogger<OnevoApiClient>.Instance);

        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by session resume
            _stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            apiClient,
            _credentials,
            _identityStore,
            null!, // EnrollmentCoordinator — not touched by session resume
            null!, // InactivityEvidenceHandler — not touched by session resume
            null!, // EvidenceSpoolStore — not touched by session resume
            new PendingLegalChallengeStore());
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
