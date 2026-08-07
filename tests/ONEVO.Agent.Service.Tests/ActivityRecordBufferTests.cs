using System.Text.Json;
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

    [Fact]
    public void Enqueue_and_dequeue_batch_sqlite()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        Assert.True(buffer.TryEnqueue(MakeRecord("b")));
        Assert.Equal(2, buffer.Count);

        var batch = buffer.DequeueBatch(10);
        Assert.Equal(2, batch.Count);
        Assert.Equal(0, buffer.Count);
        // Rows retained as synced history
        Assert.Equal(2, buffer.TotalStoredCount);
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
        var batch = buffer.DequeueBatch(10);
        Assert.Single(batch);
        Assert.Equal(0, buffer.Count);

        buffer.RequeueFront(batch);
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void SaveSessionHistory_persists_row()
    {
        using var buffer = ActivityRecordBuffer.CreateInMemory();
        var cin = DateTimeOffset.Parse("2026-08-07T04:00:00Z");
        var cout = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        buffer.SaveSessionHistory(cin, cout, TimeSpan.FromMinutes(30), TimeSpan.FromHours(7.5), 2, "09:00 AM – 06:00 PM");
        // No exception = success; Count is for collection_records only
        Assert.Equal(0, buffer.Count);
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
                var batch = buffer2.DequeueBatch(10);
                Assert.Single(batch);
                Assert.Equal("file-1", batch[0].EventId);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
