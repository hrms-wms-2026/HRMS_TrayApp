namespace ONEVO.Agent.Shared.Models;

using System.Text.Json;

public static class CollectionRecordTypes
{
    public const string ActivitySnapshot    = "activity_snapshot";
    public const string AppUsageSnapshot    = "app_usage_snapshot";
    public const string DeviceStateSnapshot = "device_state_snapshot";
    public const string Screenshot          = "screenshot";
    public const string FacePhoto           = "face_photo";
    public const string WorkSession         = "work_session";
    public const string InactivityCaptureAttempt = "inactivity_capture_attempt";
    public const string MeetingSignal       = "meeting_signal";
}

public static class CollectionSchemaVersions
{
    public const string ActivitySnapshotV1    = "1.0";
    public const string AppUsageSnapshotV1    = "1.0";
    public const string DeviceStateSnapshotV1 = "1.0";
    public const string ScreenshotV1          = "1.0";
    public const string FacePhotoV1           = "1.0";
    public const string WorkSessionV1         = "1.0";
    public const string InactivityCaptureAttemptV1 = "1.0";
    public const string MeetingSignalV1       = "1.0";
}

/// <summary>
/// A completed clock-in/break/clock-out session, queued once at ClockOut. SessionId
/// (== the enclosing CollectionRecord.EventId) is the idempotency key the backend
/// upserts on, so a retried delivery after a dropped response never double-inserts.
/// </summary>
public sealed record WorkSessionPayload
{
    public required Guid SessionId { get; init; }
    public required DateTimeOffset ClockInAt { get; init; }
    public required DateTimeOffset ClockOutAt { get; init; }
    public required TimeSpan AccumulatedBreak { get; init; }
    public required TimeSpan AccumulatedWork { get; init; }
    public required int BreakSessionCount { get; init; }
    public string? ScheduleDisplay { get; init; }
}

/// <summary>
/// Phase 1 probabilistic meeting-app-presence sample (§7.4). ProcessName identifies
/// which known meeting app was found running - never proof of an active meeting.
/// </summary>
public sealed record MeetingSignalPayload
{
    public required DateTimeOffset CapturedAt { get; init; }
    public required bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
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
