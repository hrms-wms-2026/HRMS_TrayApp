namespace ONEVO.Agent.Shared.Models;

/// <summary>
/// One periodic screenshot captured by the tray's autonomous collector.
/// Schema aligns with backend POST /api/v1/monitoring/tray/screenshots.
/// </summary>
public sealed record ScreenshotPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required string Format { get; init; }
    /// <summary>Base64-encoded image bytes.</summary>
    public required string Data { get; init; }
}
