using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Shared.Tests;

public class AppUsageSnapshotPayloadTests
{
    [Fact]
    public void Serializes_process_name_and_title_hash()
    {
        var payload = new AppUsageSnapshotPayload
        {
            CapturedAt      = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero),
            ProcessName     = "code.exe",
            WindowTitleHash = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("code.exe", root.GetProperty("ProcessName").GetString());
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            root.GetProperty("WindowTitleHash").GetString());

        // Privacy: raw title must never appear
        Assert.False(root.TryGetProperty("WindowTitle", out _));
        Assert.False(root.TryGetProperty("RawTitle", out _));
    }

    [Fact]
    public void CollectionRecord_roundtrips_app_usage_payload()
    {
        var snap = new AppUsageSnapshotPayload
        {
            CapturedAt      = DateTimeOffset.UtcNow,
            ProcessName     = "teams.exe",
            WindowTitleHash = "abc123"
        };

        var record = new CollectionRecord
        {
            EventId          = Guid.NewGuid().ToString("N"),
            RecordType       = CollectionRecordTypes.AppUsageSnapshot,
            SchemaVersion    = CollectionSchemaVersions.AppUsageSnapshotV1,
            CaptureTimestamp = snap.CapturedAt,
            DeviceId         = "test-device",
            Payload          = JsonSerializer.SerializeToElement(snap)
        };

        var restored = record.Payload.Deserialize<AppUsageSnapshotPayload>();
        Assert.NotNull(restored);
        Assert.Equal("teams.exe", restored!.ProcessName);
        Assert.Equal("abc123", restored.WindowTitleHash);
        Assert.Equal(CollectionRecordTypes.AppUsageSnapshot, record.RecordType);
        Assert.Equal(CollectionSchemaVersions.AppUsageSnapshotV1, record.SchemaVersion);
    }

    [Fact]
    public void Null_process_name_and_empty_hash_are_allowed()
    {
        var payload = new AppUsageSnapshotPayload
        {
            CapturedAt      = DateTimeOffset.UtcNow,
            ProcessName     = null,
            WindowTitleHash = string.Empty
        };

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<AppUsageSnapshotPayload>(json);
        Assert.Null(restored!.ProcessName);
        Assert.Equal(string.Empty, restored.WindowTitleHash);
    }
}
