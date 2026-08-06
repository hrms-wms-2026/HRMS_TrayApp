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
