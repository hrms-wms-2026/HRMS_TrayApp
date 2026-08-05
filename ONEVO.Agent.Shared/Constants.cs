namespace ONEVO.Agent.Shared;

public static class Constants
{
    public const string PipeName = "ONEVO.Agent.Pipe.v1";
    public const int MaxMessageLengthBytes = 65_536;
    public const int IpcConnectionTimeoutMs = 5_000;
    public const int NonceLengthBytes = 32;
    public const int ReconnectMaxAttempts = 5;
    public const int ReconnectBaseDelayMs = 1_000;

    /// <summary>Default activity capture interval (seconds). Max backend interval is 300.</summary>
    public const int DefaultActivitySnapshotIntervalSeconds = 60;

    /// <summary>Max keyboard/mouse events counted per interval before soft-cap (privacy + overflow).</summary>
    public const int MaxEventsPerInterval = 100_000;
}
