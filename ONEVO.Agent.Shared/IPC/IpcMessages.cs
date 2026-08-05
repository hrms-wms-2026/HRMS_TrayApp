namespace ONEVO.Agent.Shared.IPC;

using ONEVO.Agent.Shared.Models;

public static class IpcMessageTypes
{
    public const string StatusRequest  = "StatusRequest";
    public const string StatusResponse = "StatusResponse";
    public const string NonceChallenge = "NonceChallenge";
    public const string NonceResponse  = "NonceResponse";

    /// <summary>Tray → Service: one or more privacy-scrubbed collection records.</summary>
    public const string CollectionRecordSubmit = "CollectionRecordSubmit";

    /// <summary>Service → Tray: acknowledgement for a submitted batch.</summary>
    public const string CollectionRecordAck = "CollectionRecordAck";

    /// <summary>Service → Tray: effective policy for collector enablement.</summary>
    public const string PolicyPush = "PolicyPush";
}

public sealed record NonceChallengePayload(string Nonce);
public sealed record NonceResponsePayload(string Nonce);
public sealed record StatusResponsePayload(MonitoringState State, DateTimeOffset Timestamp);

public sealed record CollectionRecordSubmitPayload
{
    public required IReadOnlyList<CollectionRecord> Records { get; init; }
}

public sealed record CollectionRecordAckPayload
{
    public required int AcceptedCount { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record PolicyPushPayload
{
    public required AgentPolicy Policy { get; init; }
}
