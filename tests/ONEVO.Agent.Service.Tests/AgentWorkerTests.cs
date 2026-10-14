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
            null!, // EvidenceSpoolStore — not touched by lifecycle commands
            new PendingLegalChallengeStore()
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
    public async Task ExecuteClockIn_BackendSaysAlreadyClockedOut_ReturnsStableCodeAndRestoresFinishedDay()
    {
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy { Version = "v1", TrayClockInEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1) });
        // One clock read so the day is exactly 8h long (two UtcNow reads drift by microseconds).
        var now = DateTimeOffset.UtcNow;
        var dayIn = now.AddHours(-9);
        var dayOut = now.AddHours(-1);
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == AgentApiRoutes.TrayClockIn
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { status = 409, detail = "already_clocked_out" })
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    is_clocked_in = false,
                    completed_break_minutes = 30,
                    day_clocked_in_at_utc = dayIn,
                    day_clocked_out_at_utc = dayOut,
                })
            });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(policyCache: policyCache, apiClient: apiClient, allowLocalLifecycleWithoutFullGates: true);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.False(result.Success);
        Assert.Equal(ClockActionErrorCodes.AlreadyClockedOut, result.ErrorCode);
        Assert.Equal(MonitoringState.Stopped, result.State);
        Assert.Equal(dayIn, result.Session!.ClockInAt);
        Assert.Equal(dayOut, result.Session.ClockOutAt);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Session.AccumulatedBreak);
        Assert.Equal(TimeSpan.FromHours(8) - TimeSpan.FromMinutes(30), result.Session.AccumulatedWork);
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

    [Fact]
    public async Task HandleLocationChangePendingCheck_NoDeviceJwt_ReturnsUnenrolledWithoutCallingBackend()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend without a device JWT"));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(apiClient: apiClient, storeDeviceJwt: false);

        LocationChangePendingResultPayload? result = null;
        var envelope = new IpcEnvelope { Type = IpcMessageTypes.LocationChangePendingCheck };

        await worker.HandleLocationChangePendingCheckAsync(envelope, reply =>
        {
            if (reply.Type == IpcMessageTypes.LocationChangePendingResult)
                result = reply.Payload!.Value.Deserialize<LocationChangePendingResultPayload>();
            return Task.CompletedTask;
        });

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("UNENROLLED", result.ErrorCode);
        Assert.Null(result.Request);
    }

    [Fact]
    public async Task HandleDailyTasksRequest_NoDeviceJwt_ReturnsUnenrolledWithoutCallingBackend()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call backend without a device JWT"));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(apiClient: apiClient, storeDeviceJwt: false);

        var result = await RequestDailyTasks(worker);

        Assert.False(result.Success);
        Assert.Equal("UNENROLLED", result.ErrorCode);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task HandleDailyTasksRequest_ForwardsBackendTasks_AndTrimsLongTitles()
    {
        var longTitle = new string('x', 300);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                {"work_date":"2026-10-13","total_minutes":95,"task_count":2,"done_count":1,"tasks":[
                  {"task_id":"{{Guid.NewGuid()}}","short_id":"T-7","title":"{{longTitle}}","project_name":"Apollo",
                   "status_name":"Done","status_color":"#22C55E","progress_percent":100,"minutes_today":60,
                   "is_done":true,"is_clocked_in_now":false,"today_from_percent":40,"today_to_percent":100},
                  {"task_id":"{{Guid.NewGuid()}}","short_id":"T-8","title":"Review PR","project_name":null,
                   "status_name":"In Progress","status_color":"#3B82F6","progress_percent":40,"minutes_today":35,
                   "is_done":false,"is_clocked_in_now":true}]}
                """, System.Text.Encoding.UTF8, "application/json")
        });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(apiClient: apiClient);

        var result = await RequestDailyTasks(worker);

        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2026, 10, 13), result.WorkDate);
        Assert.Equal(95, result.TotalMinutes);
        Assert.Equal(1, result.DoneCount);
        Assert.Equal(2, result.Tasks.Count);
        Assert.Equal(DailyTaskItemPayload.MaxTitleLength, result.Tasks[0].Title.Length);
        Assert.True(result.Tasks[1].IsClockedInNow);
        Assert.Equal("In Progress", result.Tasks[1].StatusName);
        Assert.Equal(40, result.Tasks[0].TodayFromPercent);
        Assert.Equal(100, result.Tasks[0].TodayToPercent);
        Assert.Null(result.Tasks[1].TodayFromPercent);
    }

    [Fact]
    public async Task HandleDailyTasksRequest_BackendFailure_ReturnsServiceUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var worker = CreateSut(apiClient: apiClient);

        var result = await RequestDailyTasks(worker);

        Assert.False(result.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    private static async Task<DailyTasksResultPayload> RequestDailyTasks(AgentWorker worker)
    {
        DailyTasksResultPayload? result = null;
        await worker.HandleDailyTasksRequestAsync(
            new IpcEnvelope { Type = IpcMessageTypes.DailyTasksRequest, CorrelationId = "c1" },
            reply =>
            {
                Assert.Equal("c1", reply.CorrelationId);
                if (reply.Type == IpcMessageTypes.DailyTasksResult)
                    result = reply.Payload!.Value.Deserialize<DailyTasksResultPayload>();
                return Task.CompletedTask;
            });
        Assert.NotNull(result);
        return result!;
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
