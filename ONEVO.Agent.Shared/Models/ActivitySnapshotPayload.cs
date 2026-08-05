namespace ONEVO.Agent.Shared.Models;

/// <summary>
/// Privacy-minimized activity sample: keyboard/mouse counts only.
/// Never includes key codes, characters, clipboard, or mouse coordinates.
/// Schema aligns with backend POST /api/v1/monitoring/activity/snapshots.
/// </summary>
public sealed record ActivitySnapshotPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required int KeyboardEventsCount { get; init; }
    public required int MouseEventsCount { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int IdleSeconds { get; init; }
    public required decimal IntensityScore { get; init; }
    /// <summary>Process file name only (e.g. code.exe). Never a full path.</summary>
    public string? ForegroundProcessName { get; init; }
}

public sealed record ActivitySnapshotBatchPayload
{
    public required IReadOnlyList<ActivitySnapshotPayload> Snapshots { get; init; }
}
