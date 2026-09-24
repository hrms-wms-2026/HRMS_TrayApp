namespace ONEVO.Agent.Service.Sync;

/// <summary>
/// Pushes rows already stored in the local activity buffer to the main database.
/// </summary>
public interface IActivityBufferFlush
{
    Task FlushAsync(CancellationToken ct);
}
