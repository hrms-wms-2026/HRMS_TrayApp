namespace ONEVO.Agent.Shared.Models;

/// <summary>
/// Payload for a device-state sample: idle seconds and derived idle flag.
/// </summary>
public sealed record DeviceStateSnapshotPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required int IdleSeconds { get; init; }
    public required bool IsIdle { get; init; }
}
