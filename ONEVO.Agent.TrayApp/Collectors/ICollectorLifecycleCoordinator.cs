namespace ONEVO.Agent.TrayApp.Collectors;

/// <summary>
/// Drains interactive collectors — in particular, any pending inactivity prompt/capture — before
/// the Tray sends a pausing lifecycle command (<c>StartBreak</c>/<c>ClockOut</c>), and reconciles
/// collectors back on if the Service rejects that command while the authoritative state it returned
/// is still <see cref="MonitoringState.Active"/>.
/// </summary>
/// <remarks>
/// Implemented by <see cref="CollectorCoordinator"/>. Splitting this out as its own interface keeps
/// <c>ONEVO.Agent.TrayApp.ViewModels.ActiveSessionViewModel</c> — which only needs "drain, then
/// maybe resume" — decoupled from the coordinator's full policy/state reconciliation surface.
/// </remarks>
public interface ICollectorLifecycleCoordinator
{
    /// <summary>
    /// Stops all collectors, dismisses any pending inactivity prompt, waits for any already-Allowed
    /// capture and its attempt Named Pipe submission (including the Service's acknowledgement,
    /// bounded) to finish, and returns only after the final Named Pipe write completes. Call this
    /// before sending <c>StartBreak</c>/<c>ClockOut</c> so the Service durably enqueues the evidence
    /// attempt before it enqueues work-session completion.
    /// </summary>
    Task PrepareForPauseAsync(CancellationToken ct);

    /// <summary>
    /// Restarts eligible collectors under the current policy. Call this when the Service rejects a
    /// <c>StartBreak</c>/<c>ClockOut</c> request while the authoritative
    /// <see cref="MonitoringState"/> it returned is still <see cref="MonitoringState.Active"/> —
    /// <see cref="PrepareForPauseAsync"/> already stopped collectors optimistically, so they must be
    /// reconciled back on.
    /// </summary>
    Task ResumeAfterRejectedPauseAsync(CancellationToken ct);
}

/// <summary>
/// No-op <see cref="ICollectorLifecycleCoordinator"/> for callers/tests that have no real
/// coordinator to drain.
/// </summary>
internal sealed class NoOpCollectorLifecycleCoordinator : ICollectorLifecycleCoordinator
{
    public static readonly NoOpCollectorLifecycleCoordinator Instance = new();

    private NoOpCollectorLifecycleCoordinator() { }

    public Task PrepareForPauseAsync(CancellationToken ct) => Task.CompletedTask;

    public Task ResumeAfterRejectedPauseAsync(CancellationToken ct) => Task.CompletedTask;
}
