namespace ONEVO.Agent.Service.Buffer;

using ONEVO.Agent.Shared.Models;

/// <summary>A pending collection row plus its stable SQLite primary key for ack/retry.</summary>
public sealed record BufferedCollectionRecord(long RowId, CollectionRecord Record);

/// <summary>Metadata for an encrypted evidence file linked to a collection row.</summary>
public sealed record EvidenceSpoolEntry(
    string EventId,
    string? EncryptedPath,
    int EncryptedSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
