namespace ONEVO.Agent.Shared.Models;

using System.Text.Json;

public static class CollectionRecordTypes
{
    public const string ActivitySnapshot    = "activity_snapshot";
    public const string AppUsageSnapshot    = "app_usage_snapshot";
    public const string DeviceStateSnapshot = "device_state_snapshot";
    public const string Screenshot          = "screenshot";
    public const string FacePhoto           = "face_photo";
}

public static class CollectionSchemaVersions
{
    public const string ActivitySnapshotV1    = "1.0";
    public const string AppUsageSnapshotV1    = "1.0";
    public const string DeviceStateSnapshotV1 = "1.0";
    public const string ScreenshotV1          = "1.0";
    public const string FacePhotoV1           = "1.0";
}

public sealed record CollectionRecord
{
    public required string EventId { get; init; }
    public required string RecordType { get; init; }
    public required string SchemaVersion { get; init; }
    public DateTimeOffset CaptureTimestamp { get; init; }
    public required string DeviceId { get; init; }
    public required JsonElement Payload { get; init; }
}
