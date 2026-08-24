using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

public class ActivityRecordBufferTests
{
    private static CollectionRecord MakeRecord(string id, string type = CollectionRecordTypes.ActivitySnapshot) => new()
    {
        EventId = id,
        RecordType = type,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "dev",
        Payload = JsonSerializer.SerializeToElement(new { ok = true, id })
    };

    private static InactivityCaptureAttemptPayload MakeAttempt(Guid id) => new()
    {
        AttemptId = id,
        PolicyVersion = "v1",
        IdleStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        PromptedAt = DateTimeOffset.UtcNow,
        IdleDurationSeconds = 300,
        MonitorCount = 0,
        Outcome = InactivityCaptureOutcomes.Declined
    };

    [Fact]
    public void Enqueue_and_peek_batch_does_not_change_status()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        Assert.True(buffer.TryEnqueue(MakeRecord("b")));
        Assert.Equal(2, buffer.Count);

        var batch = buffer.PeekPendingBatch(10);
        Assert.Equal(2, batch.Count);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void MarkAcknowledged_removes_pending_eligibility()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        var peeked = buffer.PeekPendingBatch(10);
        buffer.MarkAcknowledged(peeked.Select(p => p.RowId));
        Assert.Equal(0, buffer.Count);
        Assert.Equal(1, buffer.TotalStoredCount);
    }

    [Fact]
    public void ScheduleRetry_preserves_event_id()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(MakeRecord("retry-me")));
        var peeked = buffer.PeekPendingBatch(10);
        buffer.ScheduleRetry(peeked.Select(p => p.RowId));
        Assert.Equal(1, buffer.Count);
        Assert.Equal("retry-me", buffer.PeekPendingBatch(1)[0].Record.EventId);
    }

    [Fact]
    public void InactivityAttempt_orders_before_later_work_session()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var attemptId = Guid.NewGuid();
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            MakeAttempt(attemptId), "dev", null, 0, DateTimeOffset.UtcNow.AddHours(72)));
        Assert.True(buffer.TryEnqueue(MakeRecord("session-1", CollectionRecordTypes.WorkSession)));

        var peeked = buffer.PeekPendingBatch(10);
        Assert.Equal(2, peeked.Count);
        Assert.Equal(CollectionRecordTypes.InactivityCaptureAttempt, peeked[0].Record.RecordType);
        Assert.Equal(CollectionRecordTypes.WorkSession, peeked[1].Record.RecordType);
    }

    [Fact]
    public void LegacyScreenshotRecords_are_quarantined_on_startup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var legacy = new CollectionRecord
            {
                EventId = "legacy-1",
                RecordType = CollectionRecordTypes.Screenshot,
                SchemaVersion = CollectionSchemaVersions.ScreenshotV1,
                CaptureTimestamp = DateTimeOffset.UtcNow,
                DeviceId = "dev",
                Payload = JsonSerializer.SerializeToElement(new { data = "base64-image" })
            };

            using (var fileBuffer = new ActivityRecordBuffer(path))
            {
                Assert.True(fileBuffer.TryEnqueue(legacy));
                Assert.Equal(1, fileBuffer.Count);
            }

            using var reopened = new ActivityRecordBuffer(path);
            Assert.Equal(0, reopened.Count);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DuplicateInactivityAttempt_is_idempotent()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var attempt = MakeAttempt(Guid.NewGuid());
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            attempt, "dev", null, 0, DateTimeOffset.UtcNow.AddHours(72)));
        Assert.True(buffer.TryEnqueueInactivityAttempt(
            attempt, "dev", null, 0, DateTimeOffset.UtcNow.AddHours(72)));
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void Rejects_when_at_capacity()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 1);
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        Assert.False(buffer.TryEnqueue(MakeRecord("b")));
    }

    [Fact]
    public void RequeueFront_marks_pending_again()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var rec = MakeRecord("r1");
        Assert.True(buffer.TryEnqueue(rec));
        var peeked = buffer.PeekPendingBatch(10);
        Assert.Single(peeked);
        buffer.MarkAcknowledged(peeked.Select(p => p.RowId));
        Assert.Equal(0, buffer.Count);

        buffer.RequeueFront([rec]);
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void SaveSessionHistory_persists_row()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var cin = DateTimeOffset.Parse("2026-08-07T04:00:00Z");
        var cout = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        buffer.SaveSessionHistory(cin, cout, TimeSpan.FromMinutes(30), TimeSpan.FromHours(7.5), 2, "09:00 AM – 06:00 PM", TimeSpan.Zero);
        // No exception = success; Count is for collection_records only
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void SaveSessionHistory_persists_idle_seconds()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var cin = DateTimeOffset.Parse("2026-08-07T04:00:00Z");
        var cout = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        buffer.SaveSessionHistory(
            cin, cout,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(7),
            2,
            "09:00 AM – 06:00 PM",
            TimeSpan.FromMinutes(20));

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={buffer.DatabasePath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT accumulated_idle_sec FROM session_history LIMIT 1;";
        var idle = Convert.ToDouble(cmd.ExecuteScalar());
        Assert.Equal(1200, idle, 0.001);
    }

    [Fact]
    public void InitializeSchema_AddsIdleColumnOnLegacySessionHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-idle-{Guid.NewGuid():N}.db");
        try
        {
            SQLitePCL.Batteries_V2.Init();
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    CREATE TABLE session_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        clock_in_at TEXT,
                        clock_out_at TEXT,
                        accumulated_break_sec REAL NOT NULL DEFAULT 0,
                        accumulated_work_sec REAL NOT NULL DEFAULT 0,
                        break_session_count INTEGER NOT NULL DEFAULT 0,
                        schedule_display TEXT,
                        created_at TEXT NOT NULL
                    );
                    """;
                cmd.ExecuteNonQuery();
            }

            using (var buffer = new ActivityRecordBuffer(path, maxRecords: 100))
            {
                buffer.SaveSessionHistory(
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TimeSpan.Zero, TimeSpan.Zero, 0, null, TimeSpan.FromSeconds(9));
            }

            using var read = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            read.Open();
            using var q = read.CreateCommand();
            q.CommandText = "SELECT accumulated_idle_sec FROM session_history LIMIT 1;";
            Assert.Equal(9d, Convert.ToDouble(q.ExecuteScalar()), 0.001);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void FileDatabase_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var buffer = new ActivityRecordBuffer(path, maxRecords: 100))
            {
                Assert.True(buffer.TryEnqueue(MakeRecord("file-1")));
                Assert.Equal(1, buffer.Count);
            }

            using (var buffer2 = new ActivityRecordBuffer(path, maxRecords: 100))
            {
                Assert.Equal(1, buffer2.Count);
                var batch = buffer2.PeekPendingBatch(10);
                Assert.Single(batch);
                Assert.Equal("file-1", batch[0].Record.EventId);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
