namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;

public interface IAgentCollector
{
    string Name { get; }
    Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
