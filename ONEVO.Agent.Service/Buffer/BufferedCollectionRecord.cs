namespace ONEVO.Agent.Service.Buffer;

using ONEVO.Agent.Shared.Models;

/// <summary>A pending collection row plus its stable SQLite primary key for ack/retry.</summary>
/// <param name="OwnerKey">The employee ("tenant_id:user_id") whose device token was stored when
/// the row was queued; null for rows queued before owners were recorded (AUD-004).</param>
public sealed record BufferedCollectionRecord(long RowId, CollectionRecord Record, string? OwnerKey = null);

/// <summary>Metadata for an encrypted evidence file linked to a collection row.</summary>
public sealed record EvidenceSpoolEntry(
    string EventId,
    string? EncryptedPath,
    int EncryptedSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
