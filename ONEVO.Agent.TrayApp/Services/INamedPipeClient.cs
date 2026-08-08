namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public interface INamedPipeClient
{
    event Action? OnDisconnected;
    event Action<MonitoringState>? OnStateReceived;
    event Action<StatusResponsePayload>? OnStatusReceived;
    event Action<AgentPolicy>? OnPolicyReceived;

    /// <summary>Last status received from the service — null until first response arrives.</summary>
    StatusResponsePayload? LastKnownStatus { get; }

    /// <summary>Last policy pushed from the service — null until first PolicyPush arrives.</summary>
    AgentPolicy? LastKnownPolicy { get; }

    Task StartAsync(CancellationToken ct);
    Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct);
    Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Sends a lifecycle command and waits for the correlated LifecycleResult (or timeout).
    /// </summary>
    Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null);

    /// <summary>
    /// Submits activation code and waits for EnrollmentResult (or timeout).
    /// </summary>
    Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct);

    /// <summary>
    /// Requests sign-out and waits for LogoutResult (or timeout).
    /// </summary>
    Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct);
}
