namespace ONEVO.Agent.Service.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int PolicyRefreshIntervalSeconds { get; set; } = 3600;
    public int IngestIntervalSeconds { get; set; } = 150;
    public int QueueCapacityMb { get; set; } = 100;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public int MaxReconnectAttempts { get; set; } = 5;
}
