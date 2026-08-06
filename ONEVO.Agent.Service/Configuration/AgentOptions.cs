namespace ONEVO.Agent.Service.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int PolicyRefreshIntervalSeconds { get; set; } = 3600;
    public int IngestIntervalSeconds { get; set; } = 150;
    public int QueueCapacityMb { get; set; } = 100;
    public int QueueMaxRecords { get; set; } = 5_000;
    public int HttpTimeoutSeconds { get; set; } = 30;
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>ONEVO backend base URL, e.g. https://api.example.com</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Development only: force Unenrolled→Stopped→Active so collectors can run without full enrollment.
    /// Must stay false in production.
    /// </summary>
    public bool ForceMonitoringActive { get; set; }
}
