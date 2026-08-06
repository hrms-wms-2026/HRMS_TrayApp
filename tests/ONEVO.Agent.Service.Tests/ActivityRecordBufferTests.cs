using System.Text.Json;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

public class ActivityRecordBufferTests
{
    private static CollectionRecord MakeRecord(string id) => new()
    {
        EventId = id,
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "dev",
        Payload = JsonSerializer.SerializeToElement(new { ok = true })
    };

    [Fact]
    public void Enqueue_and_dequeue_batch()
    {
        var buffer = new ActivityRecordBuffer();
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        Assert.True(buffer.TryEnqueue(MakeRecord("b")));
        Assert.Equal(2, buffer.Count);

        var batch = buffer.DequeueBatch(10);
        Assert.Equal(2, batch.Count);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Rejects_when_at_capacity()
    {
        var buffer = new ActivityRecordBuffer(maxRecords: 1);
        Assert.True(buffer.TryEnqueue(MakeRecord("a")));
        Assert.False(buffer.TryEnqueue(MakeRecord("b")));
    }
}
