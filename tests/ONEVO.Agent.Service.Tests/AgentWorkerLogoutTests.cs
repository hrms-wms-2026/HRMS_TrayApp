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
/// AUD-011: signing out while clocked in only stopped local state — the backend stayed clocked in
/// and no work-session record was written. Logout must be refused until the employee clocks out.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerLogoutTests : IDisposable
{
    private readonly CredentialStore _credentials = TestStores.NewCredentialStore();
    private int _revokeCalls;

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
    }

    private AgentWorker BuildWorker(AgentStateMachine stateMachine)
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.ActivationRevoke)
                _revokeCalls++;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        _credentials.StoreDeviceJwt("device-jwt");
        _credentials.StoreRefreshToken("refresh-token");

        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — replies go through the callback
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance),
            _credentials,
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!,
            null!,
            null!,
            new PendingLegalChallengeStore());
    }

    private static async Task<LogoutResultPayload> LogoutAsync(AgentWorker worker)
    {
        LogoutResultPayload? result = null;
        await worker.HandleLogoutRequestAsync(
            new IpcEnvelope { Type = IpcMessageTypes.LogoutRequest, CorrelationId = "c1" },
            reply =>
            {
                if (reply.Type == IpcMessageTypes.LogoutResult)
                    result = reply.Payload!.Value.Deserialize<LogoutResultPayload>();
                return Task.CompletedTask;
            });
        return Assert.IsType<LogoutResultPayload>(result);
    }

    [Theory]
    [InlineData(MonitoringState.Active)]
    [InlineData(MonitoringState.Paused)]
    public async Task Logout_WhileClockedIn_IsRefusedAndKeepsTheSession(MonitoringState clockedInState)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        stateMachine.TryTransition(MonitoringState.Active, out _);
        if (clockedInState == MonitoringState.Paused)
            stateMachine.TryTransition(MonitoringState.Paused, out _);
        var worker = BuildWorker(stateMachine);

        var result = await LogoutAsync(worker);

        Assert.False(result.Success);
        Assert.Equal("CLOCKED_IN", result.ErrorCode);
        Assert.Equal(clockedInState, stateMachine.CurrentState);
        Assert.Equal("device-jwt", _credentials.ReadDeviceJwt());
        Assert.Equal(0, _revokeCalls);
    }

    [Fact]
    public async Task Logout_WhenClockedOut_RevokesAndClearsCredentials()
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        var worker = BuildWorker(stateMachine);

        var result = await LogoutAsync(worker);

        Assert.True(result.Success);
        Assert.Equal(MonitoringState.Unenrolled, stateMachine.CurrentState);
        Assert.Null(_credentials.ReadDeviceJwt());
        Assert.Null(_credentials.ReadRefreshToken());
        Assert.Equal(1, _revokeCalls);
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
