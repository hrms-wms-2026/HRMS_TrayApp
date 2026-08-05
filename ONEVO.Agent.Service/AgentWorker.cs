namespace ONEVO.Agent.Service;

using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
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
    private readonly AgentOptions _options;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        NamedPipeServer pipeServer,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        ActivityRecordBuffer activityBuffer,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _pipeServer = pipeServer;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _activityBuffer = activityBuffer;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyDevForceActiveIfConfigured();

        _pipeServer.MessageReceived += HandleMessageAsync;
        await _pipeServer.StartAsync(stoppingToken);
        _logger.LogInformation("ONEVO Agent Service ready. State: {State}", _stateMachine.CurrentState);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void ApplyDevForceActiveIfConfigured()
    {
        if (!_options.ForceMonitoringActive)
            return;

        // Development helper: allow interactive collectors without full enrollment UI.
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
        }
    }

    private async Task ReplyStatusAndPolicyAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var status = new IpcEnvelope
        {
            Type = IpcMessageTypes.StatusResponse,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new StatusResponsePayload(_stateMachine.CurrentState, DateTimeOffset.UtcNow))
        };
        await reply(status);

        var policy = new IpcEnvelope
        {
            Type = IpcMessageTypes.PolicyPush,
            Payload = JsonSerializer.SerializeToElement(
                new PolicyPushPayload { Policy = _policyCache.Current })
        };
        await reply(policy);
    }

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
            if (record.RecordType != CollectionRecordTypes.ActivitySnapshot)
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pipeServer.MessageReceived -= HandleMessageAsync;
        await _pipeServer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
