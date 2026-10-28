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

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// AUD-012: a clock action sent just after the access token expired failed with UNAUTHORIZED instead
/// of refreshing once and retrying. AUD-009: a 409 "already in that state" (e.g. a retried POST whose
/// first attempt committed) was shown as an error although the backend already had the outcome.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerClockActionRecoveryTests : IDisposable
{
    private readonly CredentialStore _credentials = TestStores.NewCredentialStore();
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), $"onevo-recovery-{Guid.NewGuid():N}");
    private readonly List<(string Path, string? Bearer)> _calls = [];

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
    }

    private AgentWorker Build(
        AgentStateMachine stateMachine, PresenceSession presence, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(request =>
        {
            _calls.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter));
            return respond(request);
        });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

        var identityStore = new DeviceIdentityStore(_identityDir);
        identityStore.Save(new DeviceIdentity { DeviceId = "d", AgentId = "a", TenantId = "t", DeviceFingerprint = "fp" });
        _credentials.StoreDeviceJwt("expired-jwt");
        _credentials.StoreRefreshToken("refresh-token");

        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "p", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });

        var legal = new PendingLegalChallengeStore();
        var refresher = new DeviceTokenRefresher(
            NullLogger<DeviceTokenRefresher>.Instance, stateMachine, apiClient, _credentials, identityStore, legal);

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — replies go through the callback
            stateMachine,
            policyCache,
            ActivityRecordBuffer.CreateInMemory(),
            presence,
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            apiClient,
            _credentials,
            identityStore,
            null!,
            null!,
            null!,
            legal,
            tokenRefresher: refresher);
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static AgentStateMachine Stopped()
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        return stateMachine;
    }

    private static async Task<LifecycleResultPayload> SendAsync(AgentWorker worker, LifecycleAction action)
    {
        LifecycleResultPayload? result = null;
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
            });
        return Assert.IsType<LifecycleResultPayload>(result);
    }

    private static HttpResponseMessage RefreshedTokens() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            access_token = "fresh-jwt",
            expires_in_seconds = 3600,
            refresh_token = "rotated-refresh",
            refresh_expires_in_seconds = 86400
        })
    };

    [Fact]
    public async Task ClockIn_WithExpiredToken_RefreshesOnceAndRetries()
    {
        var stateMachine = Stopped();
        var worker = Build(stateMachine, new PresenceSession(), request =>
            request.RequestUri!.AbsolutePath switch
            {
                AgentApiRoutes.ActivationRefresh => RefreshedTokens(),
                AgentApiRoutes.TrayClockIn when request.Headers.Authorization?.Parameter == "expired-jwt"
                    => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
            });

        var result = await SendAsync(worker, LifecycleAction.ClockIn);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, stateMachine.CurrentState);
        Assert.Equal(
            [(AgentApiRoutes.TrayClockIn, "expired-jwt"), (AgentApiRoutes.ActivationRefresh, null), (AgentApiRoutes.TrayClockIn, "fresh-jwt")],
            _calls);
    }

    [Fact]
    public async Task ClockIn_BackendAlreadyClockedIn_ReconcilesToActiveAndSucceeds()
    {
        var clockedInAt = DateTimeOffset.UtcNow.AddHours(-2);
        var stateMachine = Stopped();
        var presence = new PresenceSession();
        var worker = Build(stateMachine, presence, request =>
            request.RequestUri!.AbsolutePath switch
            {
                AgentApiRoutes.TrayClockIn => new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { detail = "already_clocked_in" })
                },
                AgentApiRoutes.TrayAttendanceStatus => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { is_clocked_in = true, clocked_in_at_utc = clockedInAt })
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
            });

        var result = await SendAsync(worker, LifecycleAction.ClockIn);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, stateMachine.CurrentState);
        Assert.Equal(clockedInAt, presence.Snapshot(DateTimeOffset.UtcNow).ClockInAt);
    }

    [Fact]
    public async Task StartBreak_BackendBreakAlreadyActive_ReconcilesToPausedAndSucceeds()
    {
        var stateMachine = Stopped();
        var presence = new PresenceSession();
        var breakStartedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var worker = Build(stateMachine, presence, request =>
            request.RequestUri!.AbsolutePath switch
            {
                AgentApiRoutes.TrayBreakStart => new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { detail = "break_already_active" })
                },
                AgentApiRoutes.TrayAttendanceStatus => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        is_clocked_in = true,
                        clocked_in_at_utc = DateTimeOffset.UtcNow.AddHours(-1),
                        is_on_break = true,
                        break_started_at_utc = breakStartedAt
                    })
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
            });
        Assert.True((await SendAsync(worker, LifecycleAction.ClockIn)).Success);

        var result = await SendAsync(worker, LifecycleAction.StartBreak);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Paused, stateMachine.CurrentState);
        Assert.Equal(breakStartedAt, presence.Snapshot(DateTimeOffset.UtcNow).CurrentBreakStartedAt);
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
