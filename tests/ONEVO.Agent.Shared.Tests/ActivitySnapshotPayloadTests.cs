using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Shared.Tests;

public class ActivitySnapshotPayloadTests
{
    [Fact]
    public void Serializes_counts_only_fields()
    {
        var payload = new ActivitySnapshotPayload
        {
            CapturedAt = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
            KeyboardEventsCount = 42,
            MouseEventsCount = 17,
            ActiveSeconds = 50,
            IdleSeconds = 10,
            IntensityScore = 33.5m,
            ForegroundProcessName = "code.exe"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(42, root.GetProperty("KeyboardEventsCount").GetInt32());
        Assert.Equal(17, root.GetProperty("MouseEventsCount").GetInt32());
        Assert.Equal("code.exe", root.GetProperty("ForegroundProcessName").GetString());

        // Privacy: no forbidden fields
        Assert.False(root.TryGetProperty("KeyCode", out _));
        Assert.False(root.TryGetProperty("Character", out _));
        Assert.False(root.TryGetProperty("MouseX", out _));
        Assert.False(root.TryGetProperty("Clipboard", out _));
    }

    [Fact]
    public void CollectionRecord_roundtrips_activity_payload()
    {
        var snap = new ActivitySnapshotPayload
        {
            CapturedAt = DateTimeOffset.UtcNow,
            KeyboardEventsCount = 1,
            MouseEventsCount = 2,
            ActiveSeconds = 30,
            IdleSeconds = 0,
            IntensityScore = 10,
            ForegroundProcessName = null
        };

        var record = new CollectionRecord
        {
            EventId = Guid.NewGuid().ToString("N"),
            RecordType = CollectionRecordTypes.ActivitySnapshot,
            SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
            CaptureTimestamp = snap.CapturedAt,
            DeviceId = "test-device",
            Payload = JsonSerializer.SerializeToElement(snap)
        };

        var restored = record.Payload.Deserialize<ActivitySnapshotPayload>();
        Assert.NotNull(restored);
        Assert.Equal(1, restored!.KeyboardEventsCount);
        Assert.Equal(2, restored.MouseEventsCount);
        Assert.Equal(CollectionRecordTypes.ActivitySnapshot, record.RecordType);
    }
}
