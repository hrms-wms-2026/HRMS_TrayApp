namespace ONEVO.Agent.Shared.Models;

/// <summary>
/// Payload for an app-usage sample: process name + SHA-256 window title hash.
/// Raw window title is never included (§8.3).
/// </summary>
public sealed record AppUsageSnapshotPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitleHash { get; init; }
}
