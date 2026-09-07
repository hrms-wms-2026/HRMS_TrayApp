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

// This class constructs a real CredentialStore that reads/writes the shared
// %ProgramData%\ONEVO\Agent\ files — see CredentialStoreFileCollection's own doc comment.
// (DeviceIdentityStore instances here are built with a per-test temp directory override, so
// they don't need the same serialization.)
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerDevicePairingTests
{
    private static AgentWorker BuildWorker(HttpMessageHandler handler, DeviceIdentityStore? deviceIdentityStore = null)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Unenrolled, out _);

        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — pairing pushes go through the injected pushResult delegate directly in these tests
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            apiClient,
            new CredentialStore(),
            deviceIdentityStore ?? new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!, // EnrollmentCoordinator — not touched by device pairing
            null!, // InactivityEvidenceHandler — not touched by device pairing
            null!  // EvidenceSpoolStore — not touched by device pairing
        );
    }

    private static Task NoDelay(TimeSpan span, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task HandleDevicePairingStartAsync_Success_RepliesWithVerificationUri()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.DeviceAuthorizationStart)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        device_code = "device-secret",
                        user_code = "ABCD2345",
                        verification_uri = "https://localhost:4200/device/activate",
                        verification_uri_complete = "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
                        expires_in_seconds = 600,
                        interval_seconds = 5,
                    })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { code = "authorization_pending" })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingStartedPayload? result = null;
        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.DevicePairingStart,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartPayload("Laptop", "Windows", "1.0.0"))
        };
        await worker.HandleDevicePairingStartAsync(envelope, reply =>
        {
            if (reply.Type == IpcMessageTypes.DevicePairingStarted)
                result = reply.Payload!.Value.Deserialize<DevicePairingStartedPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345", result.VerificationUriComplete);
    }

    [Fact]
    public async Task HandleDevicePairingStartAsync_RemembersTenantFromPriorConnect_PrependsSubdomain()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                device_code = "device-secret",
                user_code = "ABCD2345",
                verification_uri = "https://localhost:4200/device/activate",
                verification_uri_complete = "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
                expires_in_seconds = 600,
                interval_seconds = 5,
            })
        });
        var identityStore = new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        identityStore.Save(new DeviceIdentity
        {
            DeviceId = "device-1",
            AgentId = "agent-1",
            TenantId = Guid.NewGuid().ToString(),
            DeviceFingerprint = "fingerprint-1",
            TenantSlug = "acme"
        });
        var worker = BuildWorker(handler, identityStore);

        try
        {
            DevicePairingStartedPayload? result = null;
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.DevicePairingStart,
                Payload = JsonSerializer.SerializeToElement(new DevicePairingStartPayload("Laptop", "Windows", "1.0.0"))
            };
            await worker.HandleDevicePairingStartAsync(envelope, reply =>
            {
                if (reply.Type == IpcMessageTypes.DevicePairingStarted)
                    result = reply.Payload!.Value.Deserialize<DevicePairingStartedPayload>();
                return Task.CompletedTask;
            });

            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.Equal("https://acme.localhost:4200/device/activate", result.VerificationUri);
            Assert.Equal(
                "https://acme.localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
                result.VerificationUriComplete);
        }
        finally
        {
            identityStore.Clear();
        }
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_Authorized_EnrollsAndPushesSuccessResult()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == AgentApiRoutes.DeviceAuthorizationToken)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        access_token = "eyJ.test",
                        expires_in_seconds = 3600,
                        refresh_token = "raw-refresh",
                        refresh_expires_in_seconds = 7_776_000,
                        employee_name = "Priya Employee",
                        employee_email = "priya@test.dev",
                        employee_number = "EMP-0001",
                    })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent); // heartbeat
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.True(pushed!.Success);
        Assert.Equal("Priya Employee", pushed.EmployeeName);
        Assert.Equal(MonitoringState.Stopped, worker.CurrentStateForTest);
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_AccessDenied_PushesFailureAndStopsPolling()
    {
        var callCount = 0;
        var handler = new StubHandler(request =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { code = "access_denied" })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.False(pushed!.Success);
        Assert.Equal("ACCESS_DENIED", pushed.ErrorCode);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PollDevicePairingLoopAsync_PendingThenAuthorized_PollsUntilResolved()
    {
        var callCount = 0;
        var handler = new StubHandler(request =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { code = "authorization_pending" })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = "eyJ.test",
                    expires_in_seconds = 3600,
                    refresh_token = "raw-refresh",
                    refresh_expires_in_seconds = 7_776_000,
                })
            };
        });
        var worker = BuildWorker(handler);

        DevicePairingResultPayload? pushed = null;
        var start = new DeviceAuthorizationStartResult(true, null, "device-secret", "ABCD2345",
            "https://localhost:4200/device/activate", "https://localhost:4200/device/activate?request_id=id&user_code=ABCD2345",
            600, 5);

        await worker.PollDevicePairingLoopAsync(
            start, "fingerprint-1", CancellationToken.None, NoDelay,
            pushResult: payload => { pushed = payload; return Task.CompletedTask; });

        Assert.NotNull(pushed);
        Assert.True(pushed!.Success);
        Assert.True(callCount >= 3, $"expected at least 3 poll calls (2 pending + 1 authorized + 1 heartbeat), got {callCount}");
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
