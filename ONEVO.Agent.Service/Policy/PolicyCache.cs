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

    public AgentPolicy Current
    {
        get { lock (_lock) return _policy; }
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
