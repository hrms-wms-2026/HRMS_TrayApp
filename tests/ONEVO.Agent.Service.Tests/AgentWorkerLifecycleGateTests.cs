using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// Regression coverage for the release-hardening requirement that attendance clock-in/break
/// transitions must never depend on monitoring being enabled or reachable. LifecycleGate.CanActivate
/// aggregates nine conditions including PolicyAllowsCollection, and AgentWorker.ExecuteClockIn/
/// ExecuteEndBreak refuse the transition ("GATES_CLOSED") when CanActivate is false — so if
/// PolicyAllowsCollection were ever wired to the real, possibly fail-closed monitoring policy
/// (PolicyCache.Current), a monitoring outage or a monitoring-disabled tenant would also block
/// clock-in. ApplyEnrollmentGates deliberately keeps PolicyAllowsCollection hardcoded true
/// regardless of PolicyCache's contents (see its own doc comment, §23 gap) specifically to avoid
/// that coupling. These tests prove the decoupling holds even in the worst case: a PolicyCache that
/// was never successfully populated (Current resolves to the fail-closed CreateDefault(), i.e. a
/// simulated total monitoring/server-policy failure).
/// </summary>
public class AgentWorkerLifecycleGateTests
{
    private static AgentWorker BuildStoppedWorkerWithUnavailablePolicy(out PresenceSession presence, out LifecycleGate gate)
    {
        var stateMachine = new AgentStateMachine();
        stateMachine.TryTransition(MonitoringState.Stopped, out _);

        presence = new PresenceSession();
        gate = new LifecycleGate();

        // PolicyCache never Set() — Current resolves to CreateDefault(): every capability flag
        // false, EffectiveScope="none". This is the worst case: monitoring is completely
        // unavailable/failed, not merely one capability disabled.
        var policyCache = new PolicyCache();

        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer — not touched by lifecycle commands
            stateMachine,
            policyCache,
            ActivityRecordBuffer.CreateInMemory(),
            presence,
            gate,
            Options.Create(new AgentOptions()),
            null!, // OnevoApiClient — not touched by lifecycle commands
            null!, // CredentialStore — not touched by lifecycle commands
            new DeviceIdentityStore(),
            null!, // EnrollmentCoordinator — not touched by lifecycle commands
            null!, // InactivityEvidenceHandler — not touched by lifecycle commands
            null!  // EvidenceSpoolStore — not touched by lifecycle commands
        );

        // Mirrors what a real successful enrollment/session-resume applies before any lifecycle
        // command can be sent. PolicyAllowsCollection is set true here, independent of policyCache.
        worker.ApplyEnrollmentGates();
        return worker;
    }

    private static async Task<LifecycleResultPayload> SendLifecycleAsync(
        AgentWorker worker, LifecycleAction action)
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
    public async Task ClockIn_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out var gate);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
        Assert.True(gate.CanActivate, "the gate itself must be satisfied — PolicyAllowsCollection is decoupled from PolicyCache, not merely bypassed");
    }

    [Fact]
    public async Task EndBreak_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out _);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);
        await SendLifecycleAsync(worker, LifecycleAction.StartBreak);

        var result = await SendLifecycleAsync(worker, LifecycleAction.EndBreak);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Active, result.State);
    }

    [Fact]
    public async Task ClockOut_Succeeds_WhenMonitoringPolicyIsCompletelyUnavailable()
    {
        var worker = BuildStoppedWorkerWithUnavailablePolicy(out _, out _);
        await SendLifecycleAsync(worker, LifecycleAction.ClockIn);

        var result = await SendLifecycleAsync(worker, LifecycleAction.ClockOut);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(MonitoringState.Stopped, result.State);
    }
}
