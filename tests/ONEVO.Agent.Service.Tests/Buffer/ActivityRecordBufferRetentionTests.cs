using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Buffer;

/// <summary>
/// UAT-13: delivered rows are deleted after a de-duplication window and the file gives the space
/// back, without touching anything still to be sent, quarantined rows, or inactivity-attempt dedupe.
/// </summary>
public sealed class ActivityRecordBufferRetentionTests
{
    private static readonly DateTimeOffset PastRetention =
        DateTimeOffset.UtcNow + ActivityRecordBuffer.DeliveredRetention + TimeSpan.FromHours(1);

    private static CollectionRecord Snapshot(string? id = null, int payloadChars = 40) => new()
    {
        EventId = id ?? Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "device",
        Payload = JsonSerializer.SerializeToElement(new { captured_at = DateTimeOffset.UtcNow, pad = new string('x', payloadChars) })
    };

    private static InactivityCaptureAttemptPayload Attempt(Guid id) => new()
    {
        AttemptId = id,
        PolicyVersion = "v1",
        IdleStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        PromptedAt = DateTimeOffset.UtcNow,
        IdleDurationSeconds = 300,
        MonitorCount = 0,
        Outcome = InactivityCaptureOutcomes.Declined
    };

    private static void AcknowledgeAll(ActivityRecordBuffer buffer) =>
        buffer.MarkAcknowledged(buffer.PeekPendingBatch(100_000).Select(p => p.RowId));

    [Fact]
    public void Purge_deletes_only_delivered_rows_past_the_retention_window()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var attemptId = Guid.NewGuid();
        Assert.True(buffer.TryEnqueueInactivityAttempt(Attempt(attemptId), "device", null, 0, DateTimeOffset.UtcNow.AddHours(72)));
        Assert.True(buffer.TryEnqueue(Snapshot("delivered-1")));
        Assert.True(buffer.TryEnqueue(Snapshot("delivered-2")));
        AcknowledgeAll(buffer);                                      // attempt + 2 snapshots are synced

        Assert.True(buffer.TryEnqueue(Snapshot("quarantined")));
        buffer.QuarantineRow(buffer.PeekPendingBatch(1)[0].RowId, QuarantineReasons.CorruptPayload);
        Assert.True(buffer.TryEnqueue(Snapshot("still-pending")));

        Assert.Equal(0, buffer.PurgeDelivered(DateTimeOffset.UtcNow)); // inside the window: nothing goes
        Assert.Equal(5, buffer.TotalStoredCount);

        Assert.Equal(2, buffer.PurgeDelivered(PastRetention));

        Assert.Equal(3, buffer.TotalStoredCount);
        Assert.Equal(["still-pending"], buffer.PeekPendingBatch(10).Select(p => p.Record.EventId));
        Assert.Equal(1, buffer.QuarantinedCount);
        Assert.True(buffer.HasPendingOrSyncedAttempt(attemptId));   // attempt dedupe still works
    }

    [Fact]
    public void A_resubmitted_event_is_still_deduplicated_inside_the_window()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var record = Snapshot("same-event");
        Assert.True(buffer.TryEnqueue(record));
        AcknowledgeAll(buffer);

        buffer.PurgeDelivered(DateTimeOffset.UtcNow);
        buffer.TryEnqueue(record);                                   // e.g. the tray re-sends after a lost ack

        Assert.Equal(0, buffer.Count);                               // ignored, not uploaded twice
    }

    [Fact]
    public void New_database_uses_incremental_auto_vacuum_and_shrinks_after_purge()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-retention-{Guid.NewGuid():N}.db");
        try
        {
            using var buffer = new ActivityRecordBuffer(path, maxRecords: 100_000);
            Assert.Equal(2, AutoVacuumMode(path));                  // INCREMENTAL

            for (var i = 0; i < 3_000; i++)
                Assert.True(buffer.TryEnqueue(Snapshot(payloadChars: 400)));
            AcknowledgeAll(buffer);
            var before = new FileInfo(path).Length;

            Assert.Equal(3_000, buffer.PurgeDelivered(PastRetention));

            var after = new FileInfo(path).Length;
            Assert.True(after < before / 10, $"file did not shrink enough: {before} -> {after} bytes");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Existing_database_is_converted_on_open_and_keeps_its_pending_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-retention-legacy-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = new ActivityRecordBuffer(path))
            {
                Assert.True(first.TryEnqueue(Snapshot("pending-before-upgrade")));
            }

            // Simulate a database created before this change (no auto-vacuum).
            using (var raw = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "PRAGMA auto_vacuum = NONE; VACUUM;";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            Assert.Equal(0, AutoVacuumMode(path));

            using var reopened = new ActivityRecordBuffer(path);

            Assert.Equal(2, AutoVacuumMode(path));
            Assert.Equal(["pending-before-upgrade"], reopened.PeekPendingBatch(10).Select(p => p.Record.EventId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private static long AutoVacuumMode(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA auto_vacuum;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
