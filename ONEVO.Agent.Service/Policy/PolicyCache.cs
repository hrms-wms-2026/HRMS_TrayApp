namespace ONEVO.Agent.Service.Policy;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Holds last effective policy for offline operation and PolicyPush to Tray.
/// Phase 1 default enables activity signals when monitoring is Active.
/// </summary>
public sealed class PolicyCache
{
    private readonly Lock _lock = new();
    private AgentPolicy _policy = CreateDefault();

    /// <summary>
    /// The effective policy, computed against wall-clock time on every read. Once
    /// <see cref="AgentPolicy.ValidUntil"/> has passed — e.g. the backend is unreachable and
    /// PolicySyncService cannot refresh in time — the capture-affecting flags degrade to false
    /// so an offline agent never keeps taking screenshots/camera captures on stale authority.
    /// ActivitySignalEnabled/AppUsageEnabled are left alone: presence/app-usage tracking is not
    /// the privacy-sensitive surface this guards.
    /// </summary>
    public AgentPolicy Current
    {
        get
        {
            lock (_lock)
            {
                if (_policy.ValidUntil <= DateTimeOffset.UtcNow)
                {
                    return _policy with
                    {
                        ScreenshotEnabled = false,
                        InactivityScreenshotEnabled = false,
                        CameraVerificationEnabled = false
                    };
                }
                return _policy;
            }
        }
    }

    public void Set(AgentPolicy policy)
    {
        lock (_lock) _policy = policy;
    }

    public static AgentPolicy CreateDefault() => new()
    {
        Version = "local-default-1",
        ActivitySignalEnabled = true,
        AppUsageEnabled = true,
        ScreenshotEnabled = true,
        CameraVerificationEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddHours(24)
    };
}
