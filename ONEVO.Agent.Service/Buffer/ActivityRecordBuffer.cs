namespace ONEVO.Agent.Service.Buffer;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Phase-1 in-memory buffer for activity snapshots until encrypted SQLite lands.
/// Service-owned; Tray never writes durable state for activity.
/// FIFO; records that fail to upload are returned to the front so capture order is kept.
/// </summary>
public sealed class ActivityRecordBuffer
{
    private readonly Lock _lock = new();
    private readonly LinkedList<CollectionRecord> _records = new();
    private readonly int _maxRecords;

    public ActivityRecordBuffer(int maxRecords = 5_000)
    {
        _maxRecords = maxRecords;
    }

    public int Count
    {
        get { lock (_lock) return _records.Count; }
    }

    public bool TryEnqueue(CollectionRecord record)
    {
        lock (_lock)
        {
            if (_records.Count >= _maxRecords)
                return false;
            _records.AddLast(record);
            return true;
        }
    }

    public List<CollectionRecord> DequeueBatch(int maxCount)
    {
        lock (_lock)
        {
            var batch = new List<CollectionRecord>(Math.Min(maxCount, _records.Count));
            while (batch.Count < maxCount && _records.First is { } first)
            {
                batch.Add(first.Value);
                _records.RemoveFirst();
            }
            return batch;
        }
    }

    /// <summary>
    /// Returns records that were dequeued but not delivered to the front of the buffer, in their
    /// original order. Not subject to the capacity limit: these records were already accepted.
    /// </summary>
    public void RequeueFront(IReadOnlyList<CollectionRecord> records)
    {
        lock (_lock)
        {
            for (var i = records.Count - 1; i >= 0; i--)
                _records.AddFirst(records[i]);
        }
    }
}
