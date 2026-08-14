namespace ONEVO.Agent.Shared.IPC;

using ONEVO.Agent.Shared.Models;

/// <summary>Tray → Service: begin an evidence transfer for one inactivity capture attempt.</summary>
public sealed record EvidenceTransferStartPayload(
    InactivityCaptureAttemptPayload Attempt,
    int TotalBytes,
    int ChunkCount);

/// <summary>Tray → Service: one base64-encoded chunk of the evidence image.</summary>
public sealed record EvidenceTransferChunkPayload(Guid AttemptId, int Index, string DataBase64);

/// <summary>Tray → Service: all chunks for the attempt have been sent.</summary>
public sealed record EvidenceTransferCompletePayload(Guid AttemptId);

/// <summary>Service → Tray: acknowledgement for a completed (or rejected) evidence transfer.</summary>
public sealed record EvidenceTransferAckPayload(Guid AttemptId, bool Accepted, string? ErrorCode);
