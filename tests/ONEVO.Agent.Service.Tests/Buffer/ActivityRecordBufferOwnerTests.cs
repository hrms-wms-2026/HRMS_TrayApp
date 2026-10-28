using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Buffer;

/// <summary>
/// AUD-004: every queued row records which employee (tenant + user) produced it, so rows left
/// behind after a logout or lock can never be uploaded under the next employee's account.
/// </summary>
public sealed class ActivityRecordBufferOwnerTests
{
    private static CollectionRecord Snapshot() => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "device",
        Payload = JsonSerializer.SerializeToElement(new { captured_at = DateTimeOffset.UtcNow })
    };

    [Fact]
    public void TryEnqueue_StampsTheCurrentOwner()
    {
        var owner = "tenant-1:user-1";
        using var buffer = ActivityRecordBuffer.CreateInMemory(ownerKeyProvider: () => owner);

        Assert.True(buffer.TryEnqueue(Snapshot()));
        owner = "tenant-2:user-2";
        Assert.True(buffer.TryEnqueue(Snapshot()));

        var batch = buffer.PeekPendingBatch(10);
        Assert.Equal(["tenant-1:user-1", "tenant-2:user-2"], batch.Select(b => b.OwnerKey));
    }

    [Fact]
    public void TryEnqueuePeriodicScreenshot_StampsTheCurrentOwner()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(ownerKeyProvider: () => "tenant-1:user-1");

        Assert.True(buffer.TryEnqueuePeriodicScreenshot(
            Guid.NewGuid(), DateTimeOffset.UtcNow, "device", "spool.bin", 10, DateTimeOffset.UtcNow.AddDays(1)));

        Assert.Equal("tenant-1:user-1", Assert.Single(buffer.PeekPendingBatch(10)).OwnerKey);
    }

    [Fact]
    public void NoOwnerProvider_LeavesOwnerUnknown()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();

        Assert.True(buffer.TryEnqueue(Snapshot()));

        Assert.Null(Assert.Single(buffer.PeekPendingBatch(10)).OwnerKey);
    }

    [Fact]
    public void DatabaseFromBeforeOwnerColumn_IsMigratedAndKeepsItsRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-owner-migration-{Guid.NewGuid():N}.db");
        using (var legacy = new SqliteConnection($"Data Source={path}"))
        {
            legacy.Open();
            using var cmd = legacy.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE collection_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT NOT NULL UNIQUE,
                    record_type TEXT NOT NULL, schema_version TEXT NOT NULL, capture_ts TEXT NOT NULL,
                    device_id TEXT NOT NULL, payload_json TEXT NOT NULL, created_at TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending');
                INSERT INTO collection_records
                    (event_id, record_type, schema_version, capture_ts, device_id, payload_json, created_at)
                VALUES ('legacy-1', 'activity_snapshot', '1.0', '2026-10-27T09:00:00.0000000+00:00',
                        'device', '{}', '2026-10-27T09:00:00.0000000+00:00');
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var buffer = new ActivityRecordBuffer(path);

        var row = Assert.Single(buffer.PeekPendingBatch(10));
        Assert.Equal("legacy-1", row.Record.EventId);
        Assert.Null(row.OwnerKey);
    }
}
