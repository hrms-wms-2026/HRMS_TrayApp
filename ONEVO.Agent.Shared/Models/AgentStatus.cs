namespace ONEVO.Agent.Shared.Models;

public sealed record AgentStatus
{
    public required MonitoringState State { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
