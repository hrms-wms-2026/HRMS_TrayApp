namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;

public interface IUpdateChecker
{
    /// <summary>Returns the pending update, or null when up to date / the check failed (failures are silent by design).</summary>
    Task<UpdateCheckResultPayload?> CheckAsync(CancellationToken ct);
}
