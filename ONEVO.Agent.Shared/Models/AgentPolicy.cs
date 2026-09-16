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
    public bool BiometricEnabled { get; init; }
    public bool WebEnabled { get; init; }
    public bool PhotoRequiredEnabled { get; init; }

    /// <summary>
    /// Effective geofence radius in meters for clock-in location checks, resolved server-side
    /// from Monitoring config (employee -> work mode -> role -> position -> department -> legal
    /// entity). Null when no tier configures a radius - callers keep their own hardcoded fallback
    /// for that case, same as every other fail-safe field on this record.
    /// </summary>
    public int? AllowedRadiusMeters { get; init; }

    /// <summary>
    /// Whether the employee's current work mode has "Employee registers their own location"
    /// enabled — their first clock-in/check-in becomes their permanent reference point instead of
    /// being checked against the legal entity's office point. Drives whether "Request Location
    /// Change" makes sense to offer them (a fixed office-checked employee has no personal point to
    /// change).
    /// </summary>
    public bool SelfRegistersLocation { get; init; }

    /// <summary>
    /// Whether the employee's current work mode has "Let employee choose daily" enabled — the tray
    /// must still show the daily office/home/other confirmation screen before clock-in for them.
    /// False (the common case) means that screen is skipped entirely: their work mode already
    /// decides whether they're office- or self-registered-location-checked.
    /// </summary>
    public bool AllowsDailyLocationChoice { get; init; }

    /// <summary>
    /// The legal entity's configured office coordinates (General Settings → Office location).
    /// Null when the legal entity has no office location configured — callers must not draw any
    /// distance conclusion in that case, not just fall back to a guessed default.
    /// </summary>
    public double? OfficeLatitude { get; init; }
    public double? OfficeLongitude { get; init; }

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
