namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service.Buffer;
using Xunit;

public class ActivityRecordBufferTests
{
    [Fact]
    public void Dequeue_IsFifo()
    {
        var buffer = new ActivityRecordBuffer();
        foreach (var id in new[] { "1", "2", "3" }) buffer.TryEnqueue(ActivitySyncServiceTests.Snapshot(id));

        Assert.Equal(new[] { "1", "2" }, buffer.DequeueBatch(2).Select(r => r.EventId));
        Assert.Equal(new[] { "3" }, buffer.DequeueBatch(10).Select(r => r.EventId));
    }

    [Fact]
    public void RequeueFront_RestoresOriginalOrderAheadOfNewRecords()
    {
        // ISS-030: RequeueFront used to append to the back.
        var buffer = new ActivityRecordBuffer();
        buffer.TryEnqueue(ActivitySyncServiceTests.Snapshot("1"));
        buffer.TryEnqueue(ActivitySyncServiceTests.Snapshot("2"));
        var batch = buffer.DequeueBatch(10);
        buffer.TryEnqueue(ActivitySyncServiceTests.Snapshot("3"));

        buffer.RequeueFront(batch);

        Assert.Equal(new[] { "1", "2", "3" }, buffer.DequeueBatch(10).Select(r => r.EventId));
    }

    [Fact]
    public void TryEnqueue_RejectsAtCapacity_EvenUnderConcurrency()
    {
        var buffer = new ActivityRecordBuffer(maxRecords: 1_000);

        Parallel.For(0, 5_000, i => buffer.TryEnqueue(ActivitySyncServiceTests.Snapshot(i.ToString())));

        Assert.Equal(1_000, buffer.Count);
    }
}
