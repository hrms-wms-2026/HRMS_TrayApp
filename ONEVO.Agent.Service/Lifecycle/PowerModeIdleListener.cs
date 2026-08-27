namespace ONEVO.Agent.Service.Lifecycle;

using Microsoft.Win32;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class PowerModeIdleListener : IHostedService
{
    private readonly ILogger<PowerModeIdleListener> _logger;
    private readonly PresenceSession _session;
    private readonly ISystemPowerEvents _power;
    private readonly IIpcBroadcaster _broadcaster;
    private readonly AgentStateMachine _stateMachine;

    public PowerModeIdleListener(
        ILogger<PowerModeIdleListener> logger,
        PresenceSession session,
        ISystemPowerEvents power,
        IIpcBroadcaster broadcaster,
        AgentStateMachine stateMachine)
    {
        _logger = logger;
        _session = session;
        _power = power;
        _broadcaster = broadcaster;
        _stateMachine = stateMachine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _power.PowerModeChanged += OnPowerModeChanged;
        _logger.LogInformation("PowerModeIdleListener subscribed to PowerModeChanged");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _power.PowerModeChanged -= OnPowerModeChanged;
        return Task.CompletedTask;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        try
        {
            if (!_session.HasActiveSession)
                return;

            var now = DateTimeOffset.UtcNow;
            var changed = e.Mode switch
            {
                PowerModes.Suspend => _session.StartAutoPause(PauseReason.Idle, now),
                PowerModes.Resume => EndResume(now),
                _ => false
            };

            if (changed)
            {
                _logger.LogInformation("PowerMode {Mode} applied to presence idle", e.Mode);
                _ = BroadcastStatusAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PowerModeChanged handler failed Mode={Mode}", e.Mode);
        }
    }

    private bool EndResume(DateTimeOffset now)
    {
        var ended = _session.EndAutoPause(PauseReason.Idle, now);
        _session.ObserveInbound(now);
        return ended;
    }

    private async Task BroadcastStatusAsync()
    {
        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.StatusResponse,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                    new StatusResponsePayload(
                        _stateMachine.CurrentState,
                        DateTimeOffset.UtcNow,
                        _session.Snapshot(DateTimeOffset.UtcNow)))
            };
            await _broadcaster.BroadcastAsync(envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status after power event");
        }
    }
}
