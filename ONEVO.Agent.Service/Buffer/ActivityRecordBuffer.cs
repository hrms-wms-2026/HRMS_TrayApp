namespace ONEVO.Agent.Service.Buffer;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Durable SQLite-backed queue for collection records + session history.
/// Path (production): %LocalAppData%\ONEVO\Agent\agent_activity.db
/// </summary>
public sealed class ActivityRecordBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly SqliteConnection _conn;
    private readonly int _maxRecords;
    private readonly string _databasePath;
    private bool _disposed;

    static ActivityRecordBuffer()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public string DatabasePath => _databasePath;

    public ActivityRecordBuffer(int maxRecords = 5_000)
        : this(GetDefaultDatabasePath(), maxRecords)
    {
    }

    public ActivityRecordBuffer(string databasePath, int maxRecords = 5_000)
    {
        _maxRecords = Math.Max(1, maxRecords);
        _databasePath = databasePath;

        var isUri = databasePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && databasePath != ":memory:")
        {
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        _conn = isUri
            ? new SqliteConnection($"Data Source={databasePath}")
            : new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = databasePath == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate
            }.ToString());

        _conn.Open();
        InitializeSchema();
    }

    /// <summary>Isolated in-memory DB (unique per call).</summary>
    public static ActivityRecordBuffer CreateInMemory(int maxRecords = 5_000)
    {
        var name = $"file:onevo_mem_{Guid.NewGuid():N}?mode=memory&cache=shared";
        return new ActivityRecordBuffer(name, maxRecords);
    }

    public static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ONEVO", "Agent", "agent_activity.db");

    private void InitializeSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS collection_records (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id        TEXT    NOT NULL UNIQUE,
                record_type     TEXT    NOT NULL,
                schema_version  TEXT    NOT NULL,
                capture_ts      TEXT    NOT NULL,
                device_id       TEXT    NOT NULL,
                payload_json    TEXT    NOT NULL,
                created_at      TEXT    NOT NULL,
                status          TEXT    NOT NULL DEFAULT 'pending'
            );
            CREATE INDEX IF NOT EXISTS ix_collection_pending
                ON collection_records(status, id);

            CREATE TABLE IF NOT EXISTS session_history (
                id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                clock_in_at           TEXT,
                clock_out_at          TEXT,
                accumulated_break_sec REAL    NOT NULL DEFAULT 0,
                accumulated_work_sec  REAL    NOT NULL DEFAULT 0,
                break_session_count   INTEGER NOT NULL DEFAULT 0,
                schedule_display      TEXT,
                created_at            TEXT    NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM collection_records WHERE status = 'pending';";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }

    public int TotalStoredCount
    {
        get
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM collection_records";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }

    public bool TryEnqueue(CollectionRecord record)
    {
        lock (_gate)
        {
            if (CountUnlocked() >= _maxRecords)
                return false;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT OR IGNORE INTO collection_records
                    (event_id, record_type, schema_version, capture_ts, device_id, payload_json, created_at, status)
                VALUES
                    ($eventId, $type, $schema, $capture, $device, $payload, $created, 'pending');
                """;
            cmd.Parameters.AddWithValue("$eventId", record.EventId);
            cmd.Parameters.AddWithValue("$type", record.RecordType);
            cmd.Parameters.AddWithValue("$schema", record.SchemaVersion);
            cmd.Parameters.AddWithValue("$capture", record.CaptureTimestamp.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("$device", record.DeviceId);
            cmd.Parameters.AddWithValue("$payload", record.Payload.GetRawText());
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            var ok = cmd.ExecuteNonQuery() > 0;
            if (ok)
                EnforceSizeLimitUnlocked();
            return ok;
        }
    }

    /// <summary>
    /// packages-guide §8: if DB file &gt; 100MB, drop oldest pending rows and VACUUM.
    /// No-op for in-memory databases.
    /// </summary>
    private void EnforceSizeLimitUnlocked()
    {
        try
        {
            if (_databasePath.Contains("mode=memory", StringComparison.OrdinalIgnoreCase)
                || _databasePath == ":memory:"
                || !File.Exists(_databasePath))
                return;

            var size = new FileInfo(_databasePath).Length;
            if (size <= 100L * 1024 * 1024)
                return;

            using var del = _conn.CreateCommand();
            del.CommandText =
                """
                DELETE FROM collection_records
                WHERE id IN (
                    SELECT id FROM collection_records
                    WHERE status = 'pending'
                    ORDER BY id ASC
                    LIMIT 1000
                );
                """;
            del.ExecuteNonQuery();

            using var vac = _conn.CreateCommand();
            vac.CommandText = "VACUUM;";
            vac.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort; never fail enqueue because of vacuum.
        }
    }

    public List<CollectionRecord> DequeueBatch(int maxCount)
    {
        lock (_gate)
        {
            var batch = new List<CollectionRecord>();
            var ids = new List<long>();

            using (var select = _conn.CreateCommand())
            {
                select.CommandText =
                    """
                    SELECT id, event_id, record_type, schema_version, capture_ts, device_id, payload_json
                    FROM collection_records
                    WHERE status = 'pending'
                    ORDER BY id ASC
                    LIMIT $limit;
                    """;
                select.Parameters.AddWithValue("$limit", Math.Max(1, maxCount));

                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(reader.GetInt64(0));
                    var payloadJson = reader.GetString(6);
                    using var doc = JsonDocument.Parse(payloadJson);
                    batch.Add(new CollectionRecord
                    {
                        EventId = reader.GetString(1),
                        RecordType = reader.GetString(2),
                        SchemaVersion = reader.GetString(3),
                        CaptureTimestamp = DateTimeOffset.Parse(reader.GetString(4)),
                        DeviceId = reader.GetString(5),
                        Payload = doc.RootElement.Clone()
                    });
                }
            }

            if (ids.Count == 0)
                return batch;

            using var tx = _conn.BeginTransaction();
            using (var upd = _conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText =
                    $"UPDATE collection_records SET status = 'synced' WHERE id IN ({string.Join(",", ids)});";
                upd.ExecuteNonQuery();
            }
            tx.Commit();

            return batch;
        }
    }

    public void RequeueFront(IEnumerable<CollectionRecord> records)
    {
        foreach (var r in records)
        {
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO collection_records
                        (event_id, record_type, schema_version, capture_ts, device_id, payload_json, created_at, status)
                    VALUES
                        ($eventId, $type, $schema, $capture, $device, $payload, $created, 'pending')
                    ON CONFLICT(event_id) DO UPDATE SET status = 'pending';
                    """;
                cmd.Parameters.AddWithValue("$eventId", r.EventId);
                cmd.Parameters.AddWithValue("$type", r.RecordType);
                cmd.Parameters.AddWithValue("$schema", r.SchemaVersion);
                cmd.Parameters.AddWithValue("$capture", r.CaptureTimestamp.ToUniversalTime().ToString("O"));
                cmd.Parameters.AddWithValue("$device", r.DeviceId);
                cmd.Parameters.AddWithValue("$payload", r.Payload.GetRawText());
                cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }
    }

    public void SaveSessionHistory(
        DateTimeOffset? clockIn,
        DateTimeOffset? clockOut,
        TimeSpan accumulatedBreak,
        TimeSpan accumulatedWork,
        int breakSessionCount,
        string? scheduleDisplay)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO session_history
                    (clock_in_at, clock_out_at, accumulated_break_sec, accumulated_work_sec,
                     break_session_count, schedule_display, created_at)
                VALUES
                    ($cin, $cout, $brk, $work, $breaks, $sched, $created);
                """;
            cmd.Parameters.AddWithValue("$cin", (object?)clockIn?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cout", (object?)clockOut?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brk", accumulatedBreak.TotalSeconds);
            cmd.Parameters.AddWithValue("$work", accumulatedWork.TotalSeconds);
            cmd.Parameters.AddWithValue("$breaks", breakSessionCount);
            cmd.Parameters.AddWithValue("$sched", (object?)scheduleDisplay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private int CountUnlocked()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM collection_records WHERE status = 'pending';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }
}
