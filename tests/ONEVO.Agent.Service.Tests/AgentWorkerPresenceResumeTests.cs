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

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// AUD-001 wiring: AttendanceStatusSyncService calls ApplyPresenceActive with the backend's
/// clock-in time after a Service restart. The resulting session must show the elapsed time as
/// work (minus the backend's completed breaks), not idle.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class AgentWorkerPresenceResumeTests : IDisposable
{
    public void Dispose() => TestStores.NewCredentialStore().ClearDeviceJwt();

    private static AgentWorker BuildStoppedWorker(PresenceSession presence)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by presence reconciliation
            stateMachine,
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            presence,
            new LifecycleGate(),
            Options.Create(new AgentOptions()),
            new OnevoApiClient(null!, NullLogger<OnevoApiClient>.Instance), // no HTTP on this path
            TestStores.NewCredentialStore(),
            new DeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())),
            null!,
            null!,
            null!,
            new PendingLegalChallengeStore());
        worker.ApplyEnrollmentGates();
        return worker;
    }

    [Fact]
    public void ApplyPresenceActive_WithEarlierBackendClockIn_CountsElapsedTimeAsWork()
    {
        var presence = new PresenceSession();
        var worker = BuildStoppedWorker(presence);
        var clockedInAt = DateTimeOffset.UtcNow.AddHours(-4);

        Assert.True(worker.ApplyPresenceActive(clockedInAt, completedBreakMinutes: 30));
        var snap = presence.Snapshot(DateTimeOffset.UtcNow);

        Assert.Equal(clockedInAt, snap.ClockInAt);
        Assert.True(snap.AccumulatedIdle < TimeSpan.FromMinutes(1), $"idle={snap.AccumulatedIdle}");
        Assert.Equal(TimeSpan.FromMinutes(30), snap.AccumulatedBreak);
        Assert.True(snap.AccumulatedWork > TimeSpan.FromHours(3.45), $"work={snap.AccumulatedWork}");
    }
}
