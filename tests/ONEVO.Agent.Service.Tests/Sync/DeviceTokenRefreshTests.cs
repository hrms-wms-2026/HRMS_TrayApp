using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

#pragma warning disable CA1001 // test doubles create HttpClient; no disposal needed in test scope

namespace ONEVO.Agent.Service.Tests.Sync;

/// <summary>
/// Token refresh classification (transient vs rejected) and heartbeat refresh-on-401. Uses the
/// real DPAPI CredentialStore, so it runs in the shared credential-file collection.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class DeviceTokenRefreshTests : IDisposable
{
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), "onevo-refresh-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CredentialStore _credentials = new();
    private readonly DeviceIdentityStore _identityStore;
    private readonly AgentStateMachine _stateMachine = new();
    private readonly string _oldJwt = BuildJwt(DateTimeOffset.UtcNow.AddMinutes(30), "old");
    private readonly string _newJwt = BuildJwt(DateTimeOffset.UtcNow.AddMinutes(60), "new");

    public DeviceTokenRefreshTests()
    {
        _identityStore = new DeviceIdentityStore(_identityDir);
        _identityStore.Save(new DeviceIdentity
        {
            DeviceId = "device-1",
            AgentId = "agent-1",
            TenantId = "tenant-1",
            DeviceFingerprint = "fp-1",
        });
        _credentials.StoreDeviceJwt(_oldJwt);
        _credentials.StoreRefreshToken("old-refresh");
        _stateMachine.TryTransition(MonitoringState.Stopped, out _);
    }

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
        if (Directory.Exists(_identityDir))
            Directory.Delete(_identityDir, recursive: true);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task RefreshAsync_TransientStatus_KeepsCredentialsAndState(HttpStatusCode status)
    {
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(new HttpResponseMessage(status)) };

        var outcome = await BuildRefresher(backend).RefreshAsync(null, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Transient, outcome);
        AssertCredentialsRetained();
    }

    [Fact]
    public async Task RefreshAsync_NetworkError_KeepsCredentialsAndState()
    {
        var backend = new FakeBackend { RefreshResponse = _ => throw new HttpRequestException("offline") };

        var outcome = await BuildRefresher(backend).RefreshAsync(null, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Transient, outcome);
        AssertCredentialsRetained();
    }

    [Fact]
    public async Task RefreshAsync_Rejected_ClearsCredentialsAndLocks()
    {
        var backend = new FakeBackend
        {
            RefreshResponse = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
        };

        var outcome = await BuildRefresher(backend).RefreshAsync(null, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Rejected, outcome);
        Assert.Null(_credentials.ReadDeviceJwt());
        Assert.Null(_credentials.ReadRefreshToken());
        Assert.Null(_identityStore.Load());
        Assert.Equal(MonitoringState.Locked, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task RefreshAsync_Success_StoresRotatedTokens()
    {
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(AuthResponse(_newJwt)) };

        var outcome = await BuildRefresher(backend).RefreshAsync(null, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Succeeded, outcome);
        Assert.Equal(_newJwt, _credentials.ReadDeviceJwt());
        Assert.Equal("new-refresh", _credentials.ReadRefreshToken());
    }

    [Fact]
    public async Task RefreshAsync_TwoSimultaneous401s_CallBackendOnce()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            RefreshResponse = async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return AuthResponse(_newJwt);
            },
        };
        var refresher = BuildRefresher(backend);

        var first = refresher.RefreshAsync(_oldJwt, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = refresher.RefreshAsync(_oldJwt, CancellationToken.None);
        release.SetResult();

        var outcomes = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(outcomes, o => Assert.Equal(TokenRefreshOutcome.Succeeded, o));
        Assert.Equal(1, backend.RefreshCalls);
        Assert.Equal(_newJwt, _credentials.ReadDeviceJwt());
    }

    [Fact]
    public async Task Heartbeat401_RefreshesThenRetriesHeartbeat()
    {
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(AuthResponse(_newJwt)) };
        backend.HeartbeatResponse = token => token == _newJwt ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized;

        await BuildHeartbeat(backend).SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(1, backend.RefreshCalls);
        Assert.Equal([_oldJwt, _newJwt], backend.HeartbeatTokens);
        Assert.Equal(MonitoringState.Stopped, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task Heartbeat401_RefreshRejected_LocksWithoutRetry()
    {
        var backend = new FakeBackend
        {
            RefreshResponse = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            HeartbeatResponse = _ => HttpStatusCode.Unauthorized,
        };

        await BuildHeartbeat(backend).SendHeartbeatAsync(CancellationToken.None);

        Assert.Single(backend.HeartbeatTokens);
        Assert.Equal(MonitoringState.Locked, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task HeartbeatUnavailable_DoesNotRefreshOrClearCredentials()
    {
        var backend = new FakeBackend { HeartbeatResponse = _ => HttpStatusCode.ServiceUnavailable };

        await BuildHeartbeat(backend).SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(0, backend.RefreshCalls);
        AssertCredentialsRetained();
    }

    [Fact]
    public async Task Heartbeat_TokenNearExpiry_RefreshesBeforeSending()
    {
        // e.g. the machine slept past the 45-minute scheduled refresh.
        _credentials.StoreDeviceJwt(BuildJwt(DateTimeOffset.UtcNow.AddMinutes(-10), "expired"));
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(AuthResponse(_newJwt)) };

        await BuildHeartbeat(backend).SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(1, backend.RefreshCalls);
        Assert.Equal([_newJwt], backend.HeartbeatTokens);
    }

    [Fact]
    public async Task Heartbeat_TokenNearExpiry_RefreshTransient_StillSendsHeartbeat()
    {
        var expiring = BuildJwt(DateTimeOffset.UtcNow.AddMinutes(2), "expiring");
        _credentials.StoreDeviceJwt(expiring);
        var backend = new FakeBackend
        {
            RefreshResponse = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)),
        };

        await BuildHeartbeat(backend).SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal([expiring], backend.HeartbeatTokens);
        Assert.Equal(expiring, _credentials.ReadDeviceJwt());
        Assert.Equal(MonitoringState.Stopped, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task PolicySync401_RefreshesAndAppliesPolicyInsteadOfDisablingMonitoring()
    {
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(AuthResponse(_newJwt)) };
        backend.OtherResponse = request => request.Headers.Authorization?.Parameter == _newJwt
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    version = "policy-v9",
                    activity_signal_enabled = true,
                    valid_until = DateTimeOffset.UtcNow.AddHours(1),
                }),
            }
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var cache = new PolicyCache();
        var sut = new PolicySyncService(
            NullLogger<PolicySyncService>.Instance,
            BuildApiClient(backend),
            _credentials,
            cache,
            new NullBroadcaster(),
            new PresenceSession(),
            refresher: BuildRefresher(backend));

        await sut.RefreshOnceAsync(_oldJwt, CancellationToken.None);

        Assert.Equal(1, backend.RefreshCalls);
        Assert.Equal("policy-v9", cache.Current.Version);
    }

    [Fact]
    public async Task AttendanceSync401_RefreshesAndRetriesWithNewToken()
    {
        var backend = new FakeBackend { RefreshResponse = _ => Task.FromResult(AuthResponse(_newJwt)) };
        backend.OtherResponse = request => request.Headers.Authorization?.Parameter == _newJwt
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { is_clocked_in = true }) }
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var reconciler = new RecordingPresenceReconciler();
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            BuildApiClient(backend),
            _credentials,
            reconciler,
            refresher: BuildRefresher(backend));

        await sut.PollOnceAsync(_oldJwt, CancellationToken.None);

        Assert.Equal(1, backend.RefreshCalls);
        Assert.Contains("Active", reconciler.Calls);
    }

    [Fact]
    public void NextDelay_SuccessUsesScheduleAndFailuresBackOffToCap()
    {
        Assert.Equal(TokenRefreshService.RefreshInterval, TokenRefreshService.NextDelay(0, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(15), TokenRefreshService.NextDelay(1, 0));
        Assert.Equal(TimeSpan.FromSeconds(30), TokenRefreshService.NextDelay(2, 0));
        Assert.Equal(TokenRefreshService.MaxRetryDelay, TokenRefreshService.NextDelay(50, 0));
        Assert.True(TokenRefreshService.NextDelay(50, 0.999) <= TokenRefreshService.MaxRetryDelay * 1.2);
    }

    private void AssertCredentialsRetained()
    {
        Assert.Equal(_oldJwt, _credentials.ReadDeviceJwt());
        Assert.Equal("old-refresh", _credentials.ReadRefreshToken());
        Assert.NotNull(_identityStore.Load());
        Assert.Equal(MonitoringState.Stopped, _stateMachine.CurrentState);
    }

    private DeviceTokenRefresher BuildRefresher(FakeBackend backend) =>
        new(
            NullLogger<DeviceTokenRefresher>.Instance,
            _stateMachine,
            BuildApiClient(backend),
            _credentials,
            _identityStore,
            new PendingLegalChallengeStore());

    private HeartbeatService BuildHeartbeat(FakeBackend backend) =>
        new(
            NullLogger<HeartbeatService>.Instance,
            _stateMachine,
            BuildApiClient(backend),
            _credentials,
            BuildRefresher(backend));

    private static OnevoApiClient BuildApiClient(FakeBackend backend) =>
        new(new StubHttpClientFactory(backend), NullLogger<OnevoApiClient>.Instance);

    private static HttpResponseMessage AuthResponse(string accessToken) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                access_token = accessToken,
                expires_in_seconds = 3600,
                refresh_token = "new-refresh",
                refresh_expires_in_seconds = 7_776_000,
            }),
        };

    private static string BuildJwt(DateTimeOffset expiry, string marker)
    {
        static string Encode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Encode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(new { sub = "device-1", jti = marker, exp = expiry.ToUnixTimeSeconds() }));
        return $"{header}.{body}.unsigned";
    }

    private sealed class FakeBackend : HttpMessageHandler
    {
        private int _refreshCalls;

        public Func<HttpRequestMessage, Task<HttpResponseMessage>> RefreshResponse { get; init; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        public Func<string?, HttpStatusCode> HeartbeatResponse { get; set; } = _ => HttpStatusCode.NoContent;

        public List<string?> HeartbeatTokens { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> OtherResponse { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int RefreshCalls => Volatile.Read(ref _refreshCalls);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == AgentApiRoutes.ActivationRefresh)
            {
                Interlocked.Increment(ref _refreshCalls);
                return await RefreshResponse(request);
            }

            if (path == AgentApiRoutes.ActivationHeartbeat)
            {
                var token = request.Headers.Authorization?.Parameter;
                lock (HeartbeatTokens)
                    HeartbeatTokens.Add(token);
                return new HttpResponseMessage(HeartbeatResponse(token));
            }

            return OtherResponse(request);
        }
    }

    private sealed class NullBroadcaster : IIpcBroadcaster
    {
        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };
    }
}
