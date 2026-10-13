namespace ONEVO.Agent.Service.Buffer;

public enum BufferHealthLevel
{
    /// <summary>Below the warning threshold.</summary>
    Ok,
    /// <summary>At or above <see cref="ActivityRecordBuffer.WarningPercent"/>% of the record or size limit.</summary>
    Warning,
    /// <summary>Low-priority capture is being refused; lifecycle records still use the reserve.</summary>
    Full,
    /// <summary>The last write failed (disk full, I/O error); nothing was deleted.</summary>
    StorageError
}

public sealed record BufferHealth(
    BufferHealthLevel Level,
    int Pending,
    int Quarantined,
    long DatabaseBytes,
    int LowPriorityLimit,
    int HardLimit,
    long MaxDatabaseBytes);
