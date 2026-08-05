namespace ONEVO.Agent.Service.Api;

using System.Text.Json.Serialization;

/// <summary>Wire format for backend POST /api/v1/monitoring/activity/snapshots.</summary>
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
