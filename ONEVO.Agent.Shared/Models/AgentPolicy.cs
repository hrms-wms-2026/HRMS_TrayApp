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

    /// <summary>
    /// Legal-entity-local scheduled work start/end (from General Settings' Default work hours).
    /// Null when the legal entity has no schedule configured — the tray must not fall back to a
    /// hardcoded display in that case.
    /// </summary>
    public TimeOnly? ScheduleStart { get; init; }
    public TimeOnly? ScheduleEnd { get; init; }

    public DateTimeOffset ValidUntil { get; init; }

    /// <summary>
    /// Human-readable "hh:mm tt – hh:mm tt" rendering of <see cref="ScheduleStart"/>/
    /// <see cref="ScheduleEnd"/>, or an honest "not configured" label when the legal entity has
    /// no Default work hours set — never a guessed/hardcoded time range.
    /// </summary>
    public string ScheduleDisplay =>
        ScheduleStart is { } start && ScheduleEnd is { } end
            ? $"{start:hh:mm tt} – {end:hh:mm tt}"
            : "Not configured";
}
