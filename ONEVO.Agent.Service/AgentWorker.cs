namespace ONEVO.Agent.Service;

using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly NamedPipeServer _pipeServer;
    private readonly AgentStateMachine _stateMachine;
    private readonly PolicyCache _policyCache;
    private readonly ActivityRecordBuffer _activityBuffer;
    private readonly PresenceSession _presenceSession;
    private readonly LifecycleGate _lifecycleGate;
    private readonly AgentOptions _options;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        NamedPipeServer pipeServer,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        ActivityRecordBuffer activityBuffer,
        PresenceSession presenceSession,
        LifecycleGate lifecycleGate,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _pipeServer = pipeServer;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _activityBuffer = activityBuffer;
        _presenceSession = presenceSession;
        _lifecycleGate = lifecycleGate;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyDevBootstrapIfConfigured();

        _presenceSession.SetScheduleDisplay(_options.DefaultScheduleDisplay);

        _pipeServer.MessageReceived += HandleMessageAsync;
        await _pipeServer.StartAsync(stoppingToken);
        _logger.LogInformation("ONEVO Agent Service ready. State: {State}", _stateMachine.CurrentState);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void ApplyDevBootstrapIfConfigured()
    {
        // Prefer lifecycle-driven Active over auto-force so Clock In UI works.
        if (_options.AllowLocalLifecycleWithoutFullGates)
        {
            // Ensure we can clock in from Stopped (leave Unenrolled → Stopped for demo).
            if (_stateMachine.CurrentState == MonitoringState.Unenrolled)
                _stateMachine.TryTransition(MonitoringState.Stopped, out _);

            _lifecycleGate.SetDeviceEnrolled(true);
            _lifecycleGate.SetCredentialValid(true);
            _lifecycleGate.SetDeviceApproved(true);
            _lifecycleGate.SetEmployeeSessionActive(true);
            _lifecycleGate.SetConsentValid(true);
            _lifecycleGate.SetPolicyAllowsCollection(true);
            _lifecycleGate.SetNotOnApprovedTimeOff(true);
            // Presence + not-on-break set on ClockIn.
            _logger.LogWarning(
                "AllowLocalLifecycleWithoutFullGates=true — local ClockIn/Break/ClockOut enabled (development)");
            return;
        }

        if (!_options.ForceMonitoringActive)
            return;

        _stateMachine.TryTransition(MonitoringState.Stopped, out _);
        _stateMachine.TryTransition(MonitoringState.Active, out _);
        _logger.LogWarning(
            "ForceMonitoringActive=true — monitoring forced Active (development only)");
    }

    private async Task HandleMessageAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        switch (envelope.Type)
        {
            case IpcMessageTypes.StatusRequest:
                await ReplyStatusAndPolicyAsync(envelope, reply);
                break;

            case IpcMessageTypes.CollectionRecordSubmit:
                await HandleCollectionSubmitAsync(envelope, reply);
                break;

            case IpcMessageTypes.ActivationCodeSubmit:
                await HandleActivationCodeSubmitAsync(envelope, reply);
                break;

            case IpcMessageTypes.LifecycleCommand:
                await HandleLifecycleCommandAsync(envelope, reply);
                break;
        }
    }

    private async Task HandleLifecycleCommandAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<LifecycleCommandPayload>();
        if (payload is null)
        {
            await ReplyLifecycleAsync(envelope, reply, false, "INVALID_PAYLOAD",
                "Missing lifecycle payload.", _stateMachine.CurrentState);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (success, errorCode, message, state) = payload.Action switch
        {
            LifecycleAction.ClockIn    => ExecuteClockIn(now),
            LifecycleAction.StartBreak => ExecuteStartBreak(now),
            LifecycleAction.EndBreak   => ExecuteEndBreak(now),
            LifecycleAction.ClockOut   => ExecuteClockOut(now),
            _ => (false, "UNKNOWN_ACTION", "Unknown lifecycle action.", _stateMachine.CurrentState)
        };

        _logger.LogInformation(
            "Lifecycle {Action} Success={Success} Error={Error} State={State}",
            payload.Action, success, errorCode ?? "-", state);

        await ReplyLifecycleAsync(envelope, reply, success, errorCode, message, state);

        // Also push a status snapshot so tray navigates even if it only listens to StatusResponse.
        await reply(BuildStatusEnvelope(correlationId: null));
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockIn(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current == MonitoringState.Active)
            return (false, "ALREADY_CLOCKED_IN", "You are already clocked in.", current);
        if (current == MonitoringState.Paused)
            return (false, "ON_BREAK", "End break or clock out first.", current);
        if (current == MonitoringState.Locked)
            return (false, "LOCKED", "Agent is locked. Re-enrollment required.", current);
        if (current == MonitoringState.Unenrolled)
            return (false, "UNENROLLED", "Device is not enrolled.", current);

        // Presence session must be active before CanActivate is true.
        _lifecycleGate.SetPresenceSessionActive(true);
        _lifecycleGate.SetNotOnBreak(true);

        if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
        {
            _lifecycleGate.SetPresenceSessionActive(false);
            return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);
        }

        if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
            return (false, "INVALID_STATE", $"Cannot clock in from {current}.", current);

        _presenceSession.ClockIn(now);
        return (true, null, "Clocked in successfully.", MonitoringState.Active);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteStartBreak(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current != MonitoringState.Active)
            return (false, "INVALID_STATE", "Break is only available while working.", current);

        if (!_stateMachine.TryTransition(MonitoringState.Paused, out _))
            return (false, "INVALID_STATE", "Cannot start break.", current);

        _lifecycleGate.SetNotOnBreak(false);
        _presenceSession.StartBreak(now);
        return (true, null, "Break started.", MonitoringState.Paused);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteEndBreak(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current != MonitoringState.Paused)
            return (false, "INVALID_STATE", "You are not on a break.", current);

        _lifecycleGate.SetNotOnBreak(true);

        if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
        {
            // Keep paused if gates fail.
            _lifecycleGate.SetNotOnBreak(false);
            return (false, "GATES_CLOSED", "Cannot resume — gates not satisfied.", current);
        }

        if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
            return (false, "INVALID_STATE", "Cannot end break.", current);

        _presenceSession.EndBreak(now);
        return (true, null, "Break ended. Welcome back.", MonitoringState.Active);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockOut(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current is not (MonitoringState.Active or MonitoringState.Paused))
            return (false, "INVALID_STATE", "You are not in an active work session.", current);

        if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
            return (false, "INVALID_STATE", "Cannot clock out.", current);

        _presenceSession.ClockOut(now);
        _lifecycleGate.SetPresenceSessionActive(false);
        _lifecycleGate.SetNotOnBreak(true);
        return (true, null, "Clocked out. Workday completed.", MonitoringState.Stopped);
    }

    private async Task ReplyLifecycleAsync(
        IpcEnvelope request,
        Func<IpcEnvelope, Task> reply,
        bool success,
        string? errorCode,
        string? message,
        MonitoringState state)
    {
        var result = new LifecycleResultPayload(
            Success: success,
            ErrorCode: errorCode,
            Message: message,
            State: state,
            Session: _presenceSession.Snapshot(DateTimeOffset.UtcNow));

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.LifecycleResult,
            CorrelationId = request.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(result)
        });
    }

    private async Task ReplyStatusAndPolicyAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        await reply(BuildStatusEnvelope(envelope.CorrelationId));

        var policy = new IpcEnvelope
        {
            Type = IpcMessageTypes.PolicyPush,
            Payload = JsonSerializer.SerializeToElement(
                new PolicyPushPayload { Policy = _policyCache.Current })
        };
        await reply(policy);
    }

    private IpcEnvelope BuildStatusEnvelope(string? correlationId) =>
        new()
        {
            Type = IpcMessageTypes.StatusResponse,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.SerializeToElement(
                new StatusResponsePayload(
                    _stateMachine.CurrentState,
                    DateTimeOffset.UtcNow,
                    _presenceSession.Snapshot(DateTimeOffset.UtcNow)))
        };

    private async Task HandleCollectionSubmitAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<CollectionRecordSubmitPayload>();
        if (payload?.Records is null || payload.Records.Count == 0)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload { AcceptedCount = 0, ErrorCode = "empty" })
            });
            return;
        }

        // Only accept collection while Active (lifecycle gate).
        if (_stateMachine.CurrentState != MonitoringState.Active)
        {
            _logger.LogInformation(
                "Rejected collection submit while state={State} count={Count}",
                _stateMachine.CurrentState,
                payload.Records.Count);
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload
                    {
                        AcceptedCount = 0,
                        ErrorCode = "monitoring_not_active"
                    })
            });
            return;
        }

        if (!_policyCache.Current.ActivitySignalEnabled)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload
                    {
                        AcceptedCount = 0,
                        ErrorCode = "activity_signal_disabled"
                    })
            });
            return;
        }

        var accepted = 0;
        foreach (var record in payload.Records)
        {
            if (record.RecordType is not (CollectionRecordTypes.ActivitySnapshot
                or CollectionRecordTypes.AppUsageSnapshot
                or CollectionRecordTypes.DeviceStateSnapshot
                or CollectionRecordTypes.Screenshot
                or CollectionRecordTypes.FacePhoto))
                continue;

            if (_activityBuffer.TryEnqueue(record))
                accepted++;
            else
                _logger.LogWarning("Activity buffer full — dropping eventId={EventId}", record.EventId);
        }

        _logger.LogInformation(
            "Buffered activity records Accepted={Accepted} QueueDepth={Depth}",
            accepted,
            _activityBuffer.Count);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.CollectionRecordAck,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new CollectionRecordAckPayload { AcceptedCount = accepted })
        });
    }

    private Task HandleActivationCodeSubmitAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        // Phase 2: validate code against backend, trigger enrollment state transition
        _logger.LogInformation("ActivationCodeSubmit received — enrollment handler not yet implemented (Phase 2)");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pipeServer.MessageReceived -= HandleMessageAsync;
        await _pipeServer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
