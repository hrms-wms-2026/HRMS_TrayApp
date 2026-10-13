using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

/// <summary>
/// AGT-02: the offline buffer is bounded, but reaching a bound never deletes a queued row — new
/// low-priority capture is refused visibly instead, and lifecycle records keep a reserve.
/// </summary>
public class ActivityRecordBufferCapacityTests
{
    private static CollectionRecord Snapshot(DateTimeOffset at, string? id = null, int payloadBytes = 0) => new()
    {
        EventId = id ?? Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = at,
        DeviceId = "dev",
        Payload = JsonSerializer.SerializeToElement(new { at, filler = new string('x', payloadBytes) })
    };

    private static CollectionRecord Lifecycle(string type) => Snapshot(DateTimeOffset.UtcNow) with { RecordType = type };

    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"onevo_cap_{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
            File.Delete(file);
    }

    [Fact]
    public void Below_at_and_above_the_limit_refuses_new_snapshots_and_keeps_queued_ones()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 10);
        for (var i = 0; i < 7; i++)
            Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow.AddMinutes(-i))));
        Assert.Equal(BufferHealthLevel.Ok, buffer.GetHealth().Level);

        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
        Assert.Equal(BufferHealthLevel.Warning, buffer.GetHealth().Level); // 8/10 = 80%

        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
        Assert.Equal(BufferHealthLevel.Full, buffer.GetHealth().Level);

        Assert.False(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
        Assert.Equal(10, buffer.Count);
        Assert.Equal(10, buffer.TotalStoredCount);
    }

    [Fact]
    public void At_the_limit_stale_snapshots_are_quarantined_not_deleted_to_make_room()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 2);
        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow.AddDays(-3), "old-1")));
        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow.AddDays(-2), "old-2")));

        Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, "fresh")));

        Assert.Equal(["fresh"], buffer.PeekPendingBatch(10).Select(p => p.Record.EventId));
        Assert.Equal(2, buffer.QuarantinedCount);
        Assert.Equal(3, buffer.TotalStoredCount);
    }

    [Fact]
    public void Lifecycle_records_use_the_reserve_when_snapshots_fill_the_queue()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 3);
        for (var i = 0; i < 3; i++)
            Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow.AddMinutes(-i))));
        Assert.False(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));

        Assert.True(buffer.TryEnqueue(Lifecycle(CollectionRecordTypes.WorkSession)));
        Assert.Equal(4, buffer.Count); // nothing displaced

        // The reserve is for lifecycle records only — a scheduled screenshot cannot consume it.
        Assert.False(buffer.TryEnqueuePeriodicScreenshot(
            Guid.NewGuid(), DateTimeOffset.UtcNow, "dev", "C:\\nowhere.bin", 10, DateTimeOffset.UtcNow.AddDays(3)));
    }

    [Fact]
    public void Queued_work_sessions_are_never_removed_to_make_room()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 1, reservedRecords: 1);
        Assert.True(buffer.TryEnqueue(Lifecycle(CollectionRecordTypes.WorkSession) with { EventId = "s1" }));
        Assert.True(buffer.TryEnqueue(Lifecycle(CollectionRecordTypes.WorkSession) with { EventId = "s2" }));

        Assert.False(buffer.TryEnqueue(Lifecycle(CollectionRecordTypes.WorkSession) with { EventId = "s3" }));
        Assert.False(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
        Assert.Equal(["s1", "s2"], buffer.PeekPendingBatch(10).Select(p => p.Record.EventId));
    }

    [Fact]
    public void Ten_days_offline_at_one_sample_per_150s_loses_nothing()
    {
        // The reported failure: 5,000 rows ≈ 8.7 days at one sample per 150 s, after which data was lost.
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 5_000);
        var samples = (int)(TimeSpan.FromDays(10).TotalSeconds / 150);
        var start = DateTimeOffset.UtcNow.AddDays(-10);
        var refused = 0;
        for (var i = 0; i < samples; i++)
        {
            if (!buffer.TryEnqueue(Snapshot(start.AddSeconds(i * 150.0))))
                refused++;
        }

        Assert.Equal(0, refused);
        Assert.Equal(samples, buffer.TotalStoredCount);          // every captured row still on disk
        Assert.True(buffer.Count <= 5_000);                      // bounded
        Assert.True(buffer.Count >= (int)(TimeSpan.FromHours(23).TotalSeconds / 150)); // last day still sendable
        Assert.Equal(samples - buffer.Count, buffer.QuarantinedCount);
    }

    [Fact]
    public void Restart_near_capacity_keeps_the_limit_and_the_rows()
    {
        var path = TempDb();
        try
        {
            using (var before = new ActivityRecordBuffer(path, maxRecords: 5))
            {
                for (var i = 0; i < 4; i++)
                    Assert.True(before.TryEnqueue(Snapshot(DateTimeOffset.UtcNow.AddMinutes(-i))));
            }
            SqliteConnection.ClearAllPools();

            using var after = new ActivityRecordBuffer(path, maxRecords: 5);
            Assert.Equal(4, after.Count);
            Assert.True(after.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
            Assert.False(after.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
            Assert.Equal(5, after.Count);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Disk_full_refuses_the_write_reports_storage_error_and_deletes_nothing()
    {
        var path = TempDb();
        try
        {
            using var buffer = new ActivityRecordBuffer(path);
            Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, "kept")));
            var pagesNow = new FileInfo(path).Length / 4096;
            buffer.SetMaxPageCountForTesting(pagesNow);

            var big = Snapshot(DateTimeOffset.UtcNow, "too-big", payloadBytes: 64 * 1024);
            Assert.False(buffer.TryEnqueue(big));
            Assert.Equal(BufferHealthLevel.StorageError, buffer.GetHealth().Level);
            Assert.Equal(["kept"], buffer.PeekPendingBatch(10).Select(p => p.Record.EventId));

            buffer.SetMaxPageCountForTesting(1_000_000);
            Assert.True(buffer.TryEnqueue(big));
            Assert.NotEqual(BufferHealthLevel.StorageError, buffer.GetHealth().Level);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Size_limit_purges_delivered_rows_only_then_refuses()
    {
        var path = TempDb();
        var attemptId = Guid.NewGuid();
        try
        {
            using (var seed = new ActivityRecordBuffer(path, maxRecords: 10_000))
            {
                Assert.True(seed.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, "keep-pending")));
                Assert.True(seed.TryEnqueueInactivityAttempt(new InactivityCaptureAttemptPayload
                {
                    AttemptId = attemptId,
                    PolicyVersion = "v1",
                    IdleStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    PromptedAt = DateTimeOffset.UtcNow,
                    IdleDurationSeconds = 300,
                    MonitorCount = 0,
                    Outcome = InactivityCaptureOutcomes.Declined
                }, "dev", null, 0, DateTimeOffset.UtcNow.AddHours(72)));
                for (var i = 0; i < 100; i++)
                    Assert.True(seed.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, $"delivered-{i}", payloadBytes: 4_000)));
                seed.MarkAcknowledged(seed.PeekPendingBatch(1_000)
                    .Where(p => p.Record.EventId.StartsWith("delivered-", StringComparison.Ordinal)
                        || p.Record.RecordType == CollectionRecordTypes.InactivityCaptureAttempt)
                    .Select(p => p.RowId));
            }
            SqliteConnection.ClearAllPools();
            const long cap = 128 * 1024;
            Assert.True(new FileInfo(path).Length > cap);

            using var buffer = new ActivityRecordBuffer(path, maxRecords: 10_000, maxDatabaseBytes: cap);
            Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, "fresh")));
            Assert.Equal(["keep-pending", "fresh"], buffer.PeekPendingBatch(10).Select(p => p.Record.EventId));
            Assert.True(buffer.HasPendingOrSyncedAttempt(attemptId)); // dedupe history kept

            // Now only unsent rows fill the file: refuse rather than delete any of them.
            var refusedAt = -1;
            for (var i = 0; i < 200 && refusedAt < 0; i++)
            {
                if (!buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, $"fill-{i}", payloadBytes: 4_000)))
                    refusedAt = i;
            }

            Assert.True(refusedAt > 0);
            Assert.Equal(BufferHealthLevel.Full, buffer.GetHealth().Level);
            Assert.Equal(refusedAt + 2, buffer.Count); // keep-pending + fresh + every accepted fill row
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Corrupt_database_file_is_preserved_and_a_new_queue_starts()
    {
        var path = TempDb();
        try
        {
            File.WriteAllBytes(path, Enumerable.Repeat((byte)0x5A, 8192).ToArray());

            using var buffer = new ActivityRecordBuffer(path);
            Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow)));
            var preserved = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*");
            Assert.Single(preserved);
            Assert.Equal(8192, new FileInfo(preserved[0]).Length);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Concurrent_capture_and_upload_never_lose_or_duplicate_rows()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 100_000);
        const int perWriter = 500;
        var acknowledged = 0;

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
                Assert.True(buffer.TryEnqueue(Snapshot(DateTimeOffset.UtcNow, $"w{w}-{i}")));
        })).ToArray();
        var uploader = Task.Run(async () =>
        {
            while (!writers.All(t => t.IsCompleted) || buffer.Count > 0)
            {
                var batch = buffer.PeekPendingBatch(50);
                buffer.MarkAcknowledged(batch.Select(b => b.RowId));
                Interlocked.Add(ref acknowledged, batch.Count);
                await Task.Yield();
            }
        });

        await Task.WhenAll(writers);
        await uploader;

        Assert.Equal(4 * perWriter, acknowledged);
        Assert.Equal(4 * perWriter, buffer.TotalStoredCount);
    }
}
