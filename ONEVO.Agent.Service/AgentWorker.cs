namespace ONEVO.Agent.Service;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(ILogger<AgentWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ONEVO Agent Service started");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
