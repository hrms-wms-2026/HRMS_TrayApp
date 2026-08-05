namespace ONEVO.Agent.Service.Buffer;

using System.Collections.Concurrent;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Phase-1 in-memory buffer for activity snapshots until encrypted SQLite lands.
/// Service-owned; Tray never writes durable state for activity.
/// </summary>
public sealed class ActivityRecordBuffer
{
    private readonly ConcurrentQueue<CollectionRecord> _queue = new();
    private readonly int _maxRecords;

    public ActivityRecordBuffer(int maxRecords = 5_000)
    {
        _maxRecords = maxRecords;
    }

    public int Count => _queue.Count;

    public bool TryEnqueue(CollectionRecord record)
    {
        if (_queue.Count >= _maxRecords)
            return false;
        _queue.Enqueue(record);
        return true;
    }

    public List<CollectionRecord> DequeueBatch(int maxCount)
    {
        var batch = new List<CollectionRecord>(Math.Min(maxCount, 200));
        while (batch.Count < maxCount && _queue.TryDequeue(out var item))
            batch.Add(item);
        return batch;
    }

    public void RequeueFront(IEnumerable<CollectionRecord> records)
    {
        // ConcurrentQueue has no true front-insert; re-append for Phase 1 retry.
        foreach (var r in records)
            _queue.Enqueue(r);
    }
}
