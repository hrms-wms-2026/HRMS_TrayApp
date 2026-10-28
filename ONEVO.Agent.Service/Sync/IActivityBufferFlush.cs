namespace ONEVO.Agent.Service.Sync;

/// <summary>
/// Pushes rows already stored in the local activity buffer to the main database.
/// </summary>
public interface IActivityBufferFlush
{
    Task FlushAsync(CancellationToken ct);

    /// <summary>
    /// Asks for an upload as soon as possible without waiting for it. Returns immediately, so it is
    /// safe from the IPC read loop; the sync loop still honours its offline backoff.
    /// </summary>
    void RequestFlush();
}
