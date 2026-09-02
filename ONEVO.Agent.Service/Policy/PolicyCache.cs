namespace ONEVO.Agent.Service.Policy;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Holds the last server-authoritative monitoring policy. Missing or expired authority always
/// resolves to a fully disabled policy; production monitoring is never enabled locally.
/// </summary>
public sealed class PolicyCache
{
    private readonly Lock _lock = new();
    private AgentPolicy _policy = CreateDefault();

    public AgentPolicy Current
    {
        get
        {
            lock (_lock)
            {
                if (_policy.ValidUntil <= DateTimeOffset.UtcNow)
                    return CreateDefault();
                return _policy;
            }
        }
    }

    public void Set(AgentPolicy policy)
    {
        lock (_lock) _policy = policy;
    }

    public void Clear()
    {
        lock (_lock) _policy = CreateDefault();
    }

    /// <summary>Unavailable authority is represented by an all-disabled policy.</summary>
    public static AgentPolicy CreateDefault() => new()
    {
        Version = "server-policy-unavailable",
        LocationTrackingEnabled = false,
        ActivitySignalEnabled = false,
        AppUsageEnabled = false,
        ScreenshotEnabled = false,
        InactivityScreenshotEnabled = false,
        CameraVerificationEnabled = false,
        IdleThresholdMinutes = 2,
        // "none" (not "employee") — no server policy is in effect, so there is no active
        // scope to report. Defaulting to "employee" here would misleadingly imply a live,
        // server-authorized policy exists when monitoring is actually fail-closed/disabled.
        EffectiveScope = "none",
        ValidUntil = DateTimeOffset.MinValue
    };
}
