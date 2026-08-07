namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed class FakeNamedPipeClient : INamedPipeClient
{
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus { get; set; }
    public AgentPolicy? LastKnownPolicy { get; set; }

    public List<IReadOnlyList<CollectionRecord>> Submitted { get; } = [];
    public List<IpcEnvelope> SentEnvelopes { get; } = [];
    public List<LifecycleAction> LifecycleActions { get; } = [];

    /// <summary>Optional canned result for SendLifecycleAsync. Null = auto-success.</summary>
    public LifecycleResultPayload? NextLifecycleResult { get; set; }

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

    public Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null)
    {
        LifecycleActions.Add(action);
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.LifecycleCommand,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new LifecycleCommandPayload(action, breakReason))
        });

        if (NextLifecycleResult is not null)
            return Task.FromResult<LifecycleResultPayload?>(NextLifecycleResult);

        var state = action switch
        {
            LifecycleAction.ClockIn    => MonitoringState.Active,
            LifecycleAction.StartBreak => MonitoringState.Paused,
            LifecycleAction.EndBreak   => MonitoringState.Active,
            LifecycleAction.ClockOut   => MonitoringState.Stopped,
            _                          => MonitoringState.Stopped
        };

        var now = DateTimeOffset.UtcNow;
        var session = new SessionSnapshot(
            ClockInAt: action == LifecycleAction.ClockOut || action == LifecycleAction.ClockIn || action == LifecycleAction.StartBreak || action == LifecycleAction.EndBreak
                ? now.AddHours(-1)
                : null,
            ClockOutAt: action == LifecycleAction.ClockOut ? now : null,
            IsOnBreak: action == LifecycleAction.StartBreak,
            CurrentBreakStartedAt: action == LifecycleAction.StartBreak ? now : null,
            AccumulatedBreak: TimeSpan.FromMinutes(10),
            AccumulatedWork: TimeSpan.FromHours(1),
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: action == LifecycleAction.StartBreak ? 1 : 0);

        if (action == LifecycleAction.ClockIn)
        {
            session = session with
            {
                ClockInAt = now,
                AccumulatedBreak = TimeSpan.Zero,
                AccumulatedWork = TimeSpan.Zero,
                BreakSessionCount = 0,
                IsOnBreak = false,
                CurrentBreakStartedAt = null,
                ClockOutAt = null
            };
        }

        return Task.FromResult<LifecycleResultPayload?>(
            new LifecycleResultPayload(true, null, "ok", state, session));
    }

    public void SimulateDisconnect()              => OnDisconnected?.Invoke();
    public void SimulateState(MonitoringState s)  => OnStateReceived?.Invoke(s);
    public void SimulateStatus(StatusResponsePayload s)
    {
        OnStatusReceived?.Invoke(s);
        OnStateReceived?.Invoke(s.State);
    }
    public void SimulatePolicy(AgentPolicy p)     => OnPolicyReceived?.Invoke(p);
}
