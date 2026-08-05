namespace ONEVO.Agent.Service;

using System.Text.Json;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly NamedPipeServer _pipeServer;
    private readonly AgentStateMachine _stateMachine;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        NamedPipeServer pipeServer,
        AgentStateMachine stateMachine)
    {
        _logger = logger;
        _pipeServer = pipeServer;
        _stateMachine = stateMachine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _pipeServer.MessageReceived += HandleMessageAsync;
        await _pipeServer.StartAsync(stoppingToken);
        _logger.LogInformation("ONEVO Agent Service ready. State: {State}", _stateMachine.CurrentState);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleMessageAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        if (envelope.Type == IpcMessageTypes.StatusRequest)
        {
            var response = new IpcEnvelope
            {
                Type = IpcMessageTypes.StatusResponse,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new StatusResponsePayload(_stateMachine.CurrentState, DateTimeOffset.UtcNow))
            };
            await reply(response);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pipeServer.MessageReceived -= HandleMessageAsync;
        await _pipeServer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
