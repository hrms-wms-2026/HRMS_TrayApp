namespace ONEVO.Agent.Service.IPC;

using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Narrow seam over <see cref="NamedPipeServer"/> for callers (e.g. <c>PolicySyncService</c>)
/// that only need to push a message to every connected Tray client, not touch pipe internals.
/// </summary>
public interface IIpcBroadcaster
{
    /// <summary>Sends <paramref name="envelope"/> to every currently-authenticated connection.</summary>
    Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default);
}
