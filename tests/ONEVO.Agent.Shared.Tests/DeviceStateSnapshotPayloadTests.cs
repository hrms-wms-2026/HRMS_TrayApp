using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Shared.Tests;

public class DeviceStateSnapshotPayloadTests
{
    [Fact]
    public void Serializes_idle_fields()
    {
        var payload = new DeviceStateSnapshotPayload
        {
            CapturedAt  = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero),
            IdleSeconds = 130,
            IsIdle      = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(130, root.GetProperty("IdleSeconds").GetInt32());
        Assert.True(root.GetProperty("IsIdle").GetBoolean());

        // Privacy: no coordinates, key codes, or window info
        Assert.False(root.TryGetProperty("MouseX", out _));
        Assert.False(root.TryGetProperty("KeyCode", out _));
        Assert.False(root.TryGetProperty("WindowTitle", out _));
    }

    [Fact]
    public void CollectionRecord_roundtrips_device_state_payload()
    {
        var snap = new DeviceStateSnapshotPayload
        {
            CapturedAt  = DateTimeOffset.UtcNow,
            IdleSeconds = 45,
            IsIdle      = false
        };

        var record = new CollectionRecord
        {
            EventId          = Guid.NewGuid().ToString("N"),
            RecordType       = CollectionRecordTypes.DeviceStateSnapshot,
            SchemaVersion    = CollectionSchemaVersions.DeviceStateSnapshotV1,
            CaptureTimestamp = snap.CapturedAt,
            DeviceId         = "test-device",
            Payload          = JsonSerializer.SerializeToElement(snap)
        };

        var restored = record.Payload.Deserialize<DeviceStateSnapshotPayload>();
        Assert.NotNull(restored);
        Assert.Equal(45, restored!.IdleSeconds);
        Assert.False(restored.IsIdle);
        Assert.Equal(CollectionRecordTypes.DeviceStateSnapshot, record.RecordType);
        Assert.Equal(CollectionSchemaVersions.DeviceStateSnapshotV1, record.SchemaVersion);
    }

    [Fact]
    public void Active_device_reports_zero_idle()
    {
        var payload = new DeviceStateSnapshotPayload
        {
            CapturedAt  = DateTimeOffset.UtcNow,
            IdleSeconds = 0,
            IsIdle      = false
        };

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<DeviceStateSnapshotPayload>(json);
        Assert.Equal(0, restored!.IdleSeconds);
        Assert.False(restored.IsIdle);
    }
}
