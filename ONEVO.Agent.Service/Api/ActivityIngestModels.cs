namespace ONEVO.Agent.Service.Api;

using System.Text.Json.Serialization;

/// <summary>Wire format for POST /api/v1/monitoring/activity/snapshots.</summary>
public sealed class ActivityIngestRequest
{
    [JsonPropertyName("snapshots")]
    public List<ActivityIngestItem> Snapshots { get; set; } = [];
}

public sealed class ActivityIngestItem
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("keyboard_events_count")]
    public int KeyboardEventsCount { get; set; }

    [JsonPropertyName("mouse_events_count")]
    public int MouseEventsCount { get; set; }

    [JsonPropertyName("active_seconds")]
    public int ActiveSeconds { get; set; }

    [JsonPropertyName("idle_seconds")]
    public int IdleSeconds { get; set; }

    [JsonPropertyName("intensity_score")]
    public decimal IntensityScore { get; set; }

    [JsonPropertyName("foreground_process_name")]
    public string? ForegroundProcessName { get; set; }
}

/// <summary>Wire format for POST /api/v1/monitoring/app-usage/snapshots.</summary>
public sealed class AppUsageIngestRequest
{
    [JsonPropertyName("snapshots")]
    public List<AppUsageIngestItem> Snapshots { get; set; } = [];
}

public sealed class AppUsageIngestItem
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("process_name")]
    public string? ProcessName { get; set; }

    [JsonPropertyName("window_title_hash")]
    public string? WindowTitleHash { get; set; }
}

/// <summary>Wire format for POST /api/v1/monitoring/device-state/snapshots.</summary>
public sealed class DeviceStateIngestRequest
{
    [JsonPropertyName("snapshots")]
    public List<DeviceStateIngestItem> Snapshots { get; set; } = [];
}

public sealed class DeviceStateIngestItem
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("idle_seconds")]
    public int IdleSeconds { get; set; }

    [JsonPropertyName("is_idle")]
    public bool IsIdle { get; set; }
}

/// <summary>Wire format for POST /api/v1/monitoring/meetings/signals.</summary>
public sealed class MeetingSignalIngestRequest
{
    [JsonPropertyName("signals")]
    public List<MeetingSignalIngestItem> Signals { get; set; } = [];
}

public sealed class MeetingSignalIngestItem
{
    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; set; }

    [JsonPropertyName("is_meeting_app_running")]
    public bool IsMeetingAppRunning { get; set; }

    [JsonPropertyName("process_name")]
    public string? ProcessName { get; set; }
}

/// <summary>Wire format for POST /api/v1/monitoring/work-sessions.</summary>
public sealed class WorkSessionSubmitRequest
{
    [JsonPropertyName("session_id")]
    public Guid SessionId { get; set; }

    [JsonPropertyName("clock_in_at")]
    public DateTimeOffset ClockInAt { get; set; }

    [JsonPropertyName("clock_out_at")]
    public DateTimeOffset ClockOutAt { get; set; }

    [JsonPropertyName("accumulated_break_seconds")]
    public int AccumulatedBreakSeconds { get; set; }

    [JsonPropertyName("accumulated_work_seconds")]
    public int AccumulatedWorkSeconds { get; set; }

    [JsonPropertyName("accumulated_idle_seconds")]
    public int AccumulatedIdleSeconds { get; set; }

    [JsonPropertyName("break_session_count")]
    public int BreakSessionCount { get; set; }

    [JsonPropertyName("schedule_display")]
    public string? ScheduleDisplay { get; set; }
}

/// <summary>Multipart field names for POST /api/v1/monitoring/tray/inactivity-attempts.</summary>
public static class InactivityAttemptFormFields
{
    public const string AttemptId = "attemptId";
    public const string PolicyVersion = "policyVersion";
    public const string IdleStartedAt = "idleStartedAt";
    public const string PromptedAt = "promptedAt";
    public const string DecisionAt = "decisionAt";
    public const string CapturedAt = "capturedAt";
    public const string IdleDurationSeconds = "idleDurationSeconds";
    public const string MonitorCount = "monitorCount";
    public const string Outcome = "outcome";
    public const string FailureCode = "failureCode";
    public const string ContentType = "contentType";
    public const string Sha256 = "sha256";
    public const string File = "file";
}

/// <summary>Wire format for POST /api/v1/monitoring/check-in.</summary>
public sealed class CheckInSubmitRequest
{
    [JsonPropertyName("latitude")]             public double? Latitude            { get; set; }
    [JsonPropertyName("longitude")]            public double? Longitude           { get; set; }
    [JsonPropertyName("location_accuracy")]    public double? LocationAccuracy    { get; set; }
    [JsonPropertyName("location_address")]     public string? LocationAddress     { get; set; }
    [JsonPropertyName("device_serial_number")] public string? DeviceSerialNumber  { get; set; }
}

/// <summary>Relevant fields from POST /api/v1/monitoring/check-in response.</summary>
public sealed class CheckInSubmitResponse
{
    [JsonPropertyName("check_in_id")]        public Guid CheckInId        { get; set; }
    [JsonPropertyName("face_scan_required")] public bool FaceScanRequired { get; set; }
}
