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
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Service.Tests.Sync;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.EndToEnd;

/// <summary>
/// T15: one employee workday through the Service's real components wired together in-process —
/// AgentWorker (IPC lifecycle + collection ingest), PresenceSession, the SQLite offline queue and
/// ActivitySyncService — with only HTTP stubbed: clock in → activity → break → activity refused on
/// break → end break → activity → backend offline → reconnect → upload → clock out.
/// No real backend, named pipe, tray UI or ProgramData is touched.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class WorkdayOfflineRecoveryFlowTests : IDisposable
{
    private readonly CredentialStore _credentials = TestStores.NewCredentialStore();
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), $"onevo-e2e-identity-{Guid.NewGuid():N}");
    private readonly string _spoolDir = Path.Combine(Path.GetTempPath(), $"onevo-e2e-spool-{Guid.NewGuid():N}");
    private readonly FakeBackend _backend = new();

    public void Dispose()
    {
        _credentials.ClearDeviceJwt();
        foreach (var dir in new[] { _identityDir, _spoolDir })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Workday_WithBackendOutage_LosesNoActivityAndUploadsOnceOnReconnect()
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);
        var presence = new PresenceSession();
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var policyCache = new PolicyCache();
        policyCache.Set(new AgentPolicy
        {
            Version = "e2e-policy",
            TrayClockInEnabled = true,
            ActivitySignalEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });
        _credentials.StoreDeviceJwt("device-jwt");
        var identity = new DeviceIdentityStore(_identityDir);
        identity.Save(new DeviceIdentity { DeviceId = "device-e2e", AgentId = "agent", TenantId = "tenant", DeviceFingerprint = "fp" });

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — replies go through the callback
            stateMachine,
            policyCache,
            buffer,
            presence,
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(_backend, NullLogger<OnevoApiClient>.Instance),
            _credentials,
            identity,
            null!,
            null!,
            null!,
            new PendingLegalChallengeStore());
        worker.ApplyEnrollmentGates();
        var sync = new ActivitySyncService(
            NullLogger<ActivitySyncService>.Instance,
            buffer,
            _credentials,
            _backend,
            new PassthroughEvidenceProtector(),
            new EvidenceSpoolStore(_spoolDir),
            Options.Create(new AgentOptions { ApiBaseUrl = "https://api.example.com" }),
            policyCache);

        // Clock in.
        var clockIn = await LifecycleAsync(worker, LifecycleAction.ClockIn);
        Assert.True(clockIn.Success, clockIn.ErrorCode);
        Assert.Equal(MonitoringState.Active, stateMachine.CurrentState);
        Assert.True(presence.HasActiveSession);

        // Activity while working is queued.
        Assert.Equal(1, (await SubmitAsync(worker, Snapshot(keyboard: 11))).AcceptedCount);

        // Break: monitoring pauses and the Service refuses capture.
        Assert.True((await LifecycleAsync(worker, LifecycleAction.StartBreak)).Success);
        Assert.Equal(MonitoringState.Paused, stateMachine.CurrentState);
        var onBreak = await SubmitAsync(worker, Snapshot(keyboard: 22));
        Assert.Equal(0, onBreak.AcceptedCount);
        Assert.Equal("monitoring_not_active", onBreak.ErrorCode);

        Assert.True((await LifecycleAsync(worker, LifecycleAction.EndBreak)).Success);
        Assert.Equal(MonitoringState.Active, stateMachine.CurrentState);
        Assert.Equal(1, (await SubmitAsync(worker, Snapshot(keyboard: 33))).AcceptedCount);

        // Backend unreachable: nothing is lost, nothing is quarantined.
        _backend.Online = false;
        await sync.FlushAsync(CancellationToken.None);
        Assert.Equal(2, buffer.GetHealth().Pending);
        Assert.Equal(0, buffer.GetHealth().Quarantined);
        Assert.Empty(_backend.UploadedKeyboardCounts);

        // Reconnect: the two working-time snapshots upload exactly once; the break one never.
        _backend.Online = true;
        await sync.FlushAsync(CancellationToken.None);
        Assert.Equal(0, buffer.GetHealth().Pending);
        Assert.Equal(0, buffer.GetHealth().Quarantined);
        Assert.Equal([11, 33], _backend.UploadedKeyboardCounts.Order());

        // A further flush sends nothing again.
        await sync.FlushAsync(CancellationToken.None);
        Assert.Equal(2, _backend.UploadedKeyboardCounts.Count);

        // Clock out closes the session; the break was accounted separately from work.
        Assert.True((await LifecycleAsync(worker, LifecycleAction.ClockOut)).Success);
        Assert.Equal(MonitoringState.Stopped, stateMachine.CurrentState);
        Assert.Equal(
            [AgentApiRoutes.TrayClockIn, AgentApiRoutes.TrayBreakStart, AgentApiRoutes.TrayBreakEnd, AgentApiRoutes.TrayClockOut],
            _backend.LifecycleCalls);
    }

    private static CollectionRecord Snapshot(int keyboard) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
        DeviceId = "tray-supplied",
        Payload = JsonSerializer.SerializeToElement(new ActivitySnapshotPayload
        {
            CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            KeyboardEventsCount = keyboard,
            MouseEventsCount = 30,
            ActiveSeconds = 60,
            IdleSeconds = 0,
            IntensityScore = 0.4m
        })
    };

    private static async Task<LifecycleResultPayload> LifecycleAsync(AgentWorker worker, LifecycleAction action)
    {
        LifecycleResultPayload? result = null;
        await worker.HandleLifecycleCommandAsync(
            new IpcEnvelope
            {
                Type = IpcMessageTypes.LifecycleCommand,
                CorrelationId = Guid.NewGuid().ToString("N"),
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

    private static async Task<CollectionRecordAckPayload> SubmitAsync(AgentWorker worker, CollectionRecord record)
    {
        CollectionRecordAckPayload? ack = null;
        await worker.HandleCollectionSubmitAsync(
            new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordSubmit,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Payload = JsonSerializer.SerializeToElement(new CollectionRecordSubmitPayload { Records = [record] })
            },
            reply =>
            {
                if (reply.Type == IpcMessageTypes.CollectionRecordAck)
                    ack = reply.Payload!.Value.Deserialize<CollectionRecordAckPayload>();
                return Task.CompletedTask;
            });
        return Assert.IsType<CollectionRecordAckPayload>(ack);
    }

    /// <summary>
    /// Stands in for the ONEVO API: accepts lifecycle calls and snapshot uploads, and can go offline
    /// (connection failure, as when the network drops).
    /// </summary>
    private sealed class FakeBackend : HttpMessageHandler, IHttpClientFactory
    {
        public volatile bool Online = true;
        public List<string> LifecycleCalls { get; } = [];
        /// <summary>keyboard_events_count of every uploaded snapshot — the wire format carries no
        /// event id (the device is identified by its token), so tests tell records apart by value.</summary>
        public List<int> UploadedKeyboardCounts { get; } = [];

        public HttpClient CreateClient(string name) =>
            new(this, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!Online)
                throw new HttpRequestException("No such host is known.");

            var path = request.RequestUri!.AbsolutePath;
            if (path is AgentApiRoutes.TrayClockIn or AgentApiRoutes.TrayClockOut
                or AgentApiRoutes.TrayBreakStart or AgentApiRoutes.TrayBreakEnd)
            {
                lock (LifecycleCalls) LifecycleCalls.Add(path);
            }
            else if (path == AgentApiRoutes.ActivitySnapshots && request.Content is not null)
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                lock (UploadedKeyboardCounts)
                {
                    foreach (var snapshot in body.RootElement.GetProperty("snapshots").EnumerateArray())
                        UploadedKeyboardCounts.Add(snapshot.GetProperty("keyboard_events_count").GetInt32());
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
