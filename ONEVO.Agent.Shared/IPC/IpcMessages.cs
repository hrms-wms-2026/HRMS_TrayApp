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

    /// <summary>Tray → Service: employee requested sign-out.</summary>
    public const string LogoutRequest = "LogoutRequest";

    /// <summary>Service → Tray: result of a sign-out attempt.</summary>
    public const string LogoutResult = "LogoutResult";

    /// <summary>Tray → Service: begin an evidence transfer for one inactivity capture attempt.</summary>
    public const string EvidenceTransferStart = "EvidenceTransferStart";

    /// <summary>Tray → Service: one base64-encoded chunk of the evidence image.</summary>
    public const string EvidenceTransferChunk = "EvidenceTransferChunk";

    /// <summary>Tray → Service: all chunks for the attempt have been sent.</summary>
    public const string EvidenceTransferComplete = "EvidenceTransferComplete";

    /// <summary>Service → Tray: acknowledgement for a completed (or rejected) evidence transfer.</summary>
    public const string EvidenceTransferAck = "EvidenceTransferAck";
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
    public string? ErrorCode { get; init; }   // "INVALID_CODE" | "EXPIRED" | "ALREADY_ENROLLED" | "SERVICE_UNAVAILABLE"
    public string? EmployeeName { get; init; }   // set on success for greeting
    public string? EmployeeEmail { get; init; }  // set on success for the workspace-setup screen
    public string? EmployeeNumber { get; init; } // set on success for the workspace-setup screen
}

public sealed record LogoutResultPayload(bool Success, string? ErrorCode);
