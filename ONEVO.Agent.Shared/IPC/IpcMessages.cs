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

    /// <summary>Tray → Service: employee-entered activation code from web portal.</summary>
    public const string ActivationCodeSubmit = "ActivationCodeSubmit";

    /// <summary>Service → Tray: result of enrollment attempt.</summary>
    public const string EnrollmentResult = "EnrollmentResult";

    /// <summary>Tray → Service: employee lifecycle action (clock-in, break, clock-out).</summary>
    public const string LifecycleCommand = "LifecycleCommand";

    /// <summary>Service → Tray: result of a lifecycle action.</summary>
    public const string LifecycleResult = "LifecycleResult";
}

public enum LifecycleAction
{
    ClockIn,
    StartBreak,
    EndBreak,
    ClockOut
}

public sealed record NonceChallengePayload(string Nonce);
public sealed record NonceResponsePayload(string Nonce);

/// <summary>Authoritative presence-session snapshot owned by the Service.</summary>
public sealed record SessionSnapshot(
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    bool IsOnBreak,
    DateTimeOffset? CurrentBreakStartedAt,
    TimeSpan AccumulatedBreak,
    TimeSpan AccumulatedWork,
    string? ScheduleDisplay,
    int BreakSessionCount);

public sealed record StatusResponsePayload(
    MonitoringState State,
    DateTimeOffset Timestamp,
    SessionSnapshot? Session = null);

public sealed record LifecycleCommandPayload(
    LifecycleAction Action,
    string? BreakReason = null);

public sealed record LifecycleResultPayload(
    bool Success,
    string? ErrorCode,
    string? Message,
    MonitoringState State,
    SessionSnapshot? Session);

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

public sealed record ActivationCodeSubmitPayload(string Code);

public sealed record EnrollmentResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }   // "INVALID_CODE" | "EXPIRED" | "ALREADY_ENROLLED"
    public string? EmployeeName { get; init; } // set on success for greeting
}
