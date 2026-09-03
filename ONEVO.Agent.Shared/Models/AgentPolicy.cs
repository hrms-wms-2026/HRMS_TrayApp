namespace ONEVO.Agent.Shared.Models;

public sealed record AgentPolicy
{
    public required string Version { get; init; }
        public bool LocationTrackingEnabled { get; init; }
    public bool ActivitySignalEnabled { get; init; }

    public bool AppUsageEnabled { get; init; }
    public bool ScreenshotEnabled { get; init; }
    public bool CameraVerificationEnabled { get; init; }
    public bool InactivityScreenshotEnabled { get; init; }
    public bool TrayClockInEnabled { get; init; }

    /// <summary>
    /// The scope the server evaluated this policy against (e.g. "employee"). Mirrors the
    /// backend's effective_scope field; wire format only, not itself an enforcement flag.
    /// </summary>
    public string EffectiveScope { get; init; } = "employee";

    /// <summary>
    /// Minutes of continuous mouse/keyboard inactivity before the "Activity check" screenshot
    /// prompt fires. Defaults to 2 so every existing test/local-default fixture that constructs
    /// an AgentPolicy without setting this explicitly keeps a sane, non-zero value (0 would mean
    /// "prompt on every poll tick", which is not a safe default for anything).
    /// </summary>
    public int IdleThresholdMinutes { get; init; } = 2;

    public DateTimeOffset ValidUntil { get; init; }
}
