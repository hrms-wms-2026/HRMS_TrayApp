namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed class FakeNamedPipeClient : INamedPipeClient
{
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public List<IReadOnlyList<CollectionRecord>> Submitted { get; } = [];
    public List<IpcEnvelope> SentEnvelopes { get; } = [];

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct)
    {
        Submitted.Add(records);
        return Task.CompletedTask;
    }

    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        SentEnvelopes.Add(envelope);
        return Task.CompletedTask;
    }

    public void SimulateDisconnect()              => OnDisconnected?.Invoke();
    public void SimulateState(MonitoringState s)  => OnStateReceived?.Invoke(s);
    public void SimulatePolicy(AgentPolicy p)     => OnPolicyReceived?.Invoke(p);
}
