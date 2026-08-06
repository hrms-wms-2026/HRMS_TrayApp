namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public interface INamedPipeClient
{
    event Action? OnDisconnected;
    event Action<MonitoringState>? OnStateReceived;
    event Action<AgentPolicy>? OnPolicyReceived;

    Task StartAsync(CancellationToken ct);
    Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct);
    Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct);
}
