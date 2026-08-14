namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;

public interface IAgentCollector
{
    string Name { get; }
    Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// True while the collector is actively collecting. Most collectors only stop when told to
    /// (<see cref="StopAsync"/>), so the default is always <c>true</c> once conceptually started —
    /// implementers with no internal self-stop behavior never need to override this.
    /// <see cref="InactivityScreenshotCollector"/> is the exception: it can self-stop between
    /// <see cref="StartAsync"/> and <see cref="StopAsync"/> (its own policy-staleness check), and
    /// overrides this to report that honestly so <see cref="CollectorCoordinator"/> can detect and
    /// recover from the stall instead of assuming "started" means "still running".
    /// </summary>
    bool IsRunning => true;
}
