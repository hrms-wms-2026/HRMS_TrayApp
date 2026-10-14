namespace ONEVO.Agent.Service.Sync;

/// <summary>
/// Whether the backend is currently able to push changes to this tray: the agent-hub connection
/// is up AND the server announced <c>PushReady</c> (an older backend never does). While active,
/// the pollers drop to slow fallback intervals; the moment it drops they return to their old fast
/// cadence, so a dead connection can only ever cost freshness for one fast interval.
/// </summary>
public sealed class PushChannelState
{
    private volatile bool _active;

    public bool IsActive => _active;

    public void SetActive(bool active) => _active = active;

    /// <summary>
    /// Polls <paramref name="pollOnce"/> forever: immediately, then every <paramref name="slow"/>
    /// while push is active and every <paramref name="fast"/> otherwise. It wakes every
    /// <paramref name="fast"/> to re-check, so losing push mid-wait falls back within one fast tick.
    /// <paramref name="forceFast"/> keeps the fast cadence regardless (e.g. while on break).
    /// </summary>
    public async Task RunAdaptiveLoopAsync(
        TimeSpan fast,
        TimeSpan slow,
        Func<CancellationToken, Task> pollOnce,
        CancellationToken ct,
        Func<bool>? forceFast = null)
    {
        var lastPoll = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var interval = IsActive && forceFast?.Invoke() != true ? slow : fast;
            if (DateTimeOffset.UtcNow - lastPoll >= interval)
            {
                lastPoll = DateTimeOffset.UtcNow;
                await pollOnce(ct).ConfigureAwait(false);
            }

            await Task.Delay(fast, ct).ConfigureAwait(false);
        }
    }
}
