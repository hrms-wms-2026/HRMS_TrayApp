namespace ONEVO.Agent.Service.Buffer;

/// <summary>
/// Why a buffered row left the send queue. Stored in <c>collection_records.quarantine_reason</c>;
/// server rejections are recorded as <c>rejected:&lt;http status&gt;[:&lt;code&gt;]</c>.
/// </summary>
public static class QuarantineReasons
{
    public const string Unspecified = "unspecified";
    public const string CorruptPayload = "corrupt_payload";
    public const string StaleOver24h = "stale_over_24h";
    public const string PolicyDisabled = "policy_disabled";

    /// <summary>Queued under another employee's device token (logout/lock, then re-enrollment) — AUD-004.</summary>
    public const string OwnerMismatch = "owner_mismatch";

    /// <summary>Client clock ahead of the server; becomes sendable again, so it is requeued automatically.</summary>
    public const string FutureTime = "rejected:future_time";

    public const string RejectedPrefix = "rejected:";

    public static string Rejected(int status, string? code) =>
        string.IsNullOrWhiteSpace(code) ? $"{RejectedPrefix}{status}" : $"{RejectedPrefix}{status}:{code}";
}
