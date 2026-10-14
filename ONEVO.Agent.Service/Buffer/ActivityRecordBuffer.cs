namespace ONEVO.Agent.Service.Buffer;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Durable SQLite-backed queue for collection records + session history.
/// Path (production): %LocalAppData%\ONEVO\Agent\agent_activity.db
/// </summary>
public sealed class ActivityRecordBuffer : IDisposable
{
    private readonly object _gate = new();
    private SqliteConnection _conn;
    private readonly int _maxRecords;
    private readonly int _reservedRecords;
    private readonly long _maxDatabaseBytes;
    private readonly string _databasePath;
    private readonly ILogger _logger;
    private bool _storageFailed;
    private BufferHealthLevel _lastLoggedLevel = BufferHealthLevel.Ok;
    private bool _disposed;

    public const long DefaultMaxDatabaseBytes = 100L * 1024 * 1024;
    public const int WarningPercent = 80;

    /// <summary>
    /// The backend refuses snapshots captured more than this long ago (SnapshotTimeWindow.MaxAge),
    /// so a pending snapshot past it can never be delivered.
    /// </summary>
    public static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromHours(24);

    // High-volume, low-priority rows. Only these may be quarantined as stale to make room; nothing
    // pending is ever deleted to make room (AGT-02).
    private const string LowPriorityTypesSql =
        "('" + CollectionRecordTypes.ActivitySnapshot + "','" + CollectionRecordTypes.AppUsageSnapshot + "','"
        + CollectionRecordTypes.DeviceStateSnapshot + "','" + CollectionRecordTypes.MeetingSignal + "')";

    static ActivityRecordBuffer()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public string DatabasePath => _databasePath;

    public ActivityRecordBuffer(int maxRecords = 5_000)
        : this(GetDefaultDatabasePath(), maxRecords)
    {
    }

    /// <param name="maxRecords">Pending rows at which low-priority capture is refused.</param>
    /// <param name="maxDatabaseBytes">Hard size limit of the database file.</param>
    /// <param name="reservedRecords">
    /// Extra pending rows above <paramref name="maxRecords"/> kept for lifecycle/evidence records
    /// (work session, inactivity attempt, face photo) so a full snapshot backlog never blocks them.
    /// Defaults to 10% of <paramref name="maxRecords"/> (at least 1).
    /// </param>
    public ActivityRecordBuffer(
        string databasePath,
        int maxRecords = 5_000,
        long maxDatabaseBytes = DefaultMaxDatabaseBytes,
        int? reservedRecords = null,
        ILogger? logger = null)
    {
        _maxRecords = Math.Max(1, maxRecords);
        _reservedRecords = Math.Max(1, reservedRecords ?? _maxRecords / 10);
        _maxDatabaseBytes = Math.Max(1, maxDatabaseBytes);
        _logger = logger ?? NullLogger.Instance;
        _databasePath = databasePath;

        var isUri = databasePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && databasePath != ":memory:")
        {
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        _conn = OpenConnection(databasePath, isUri);
        try
        {
            InitializeSchema();
        }
        catch (SqliteException ex) when (IsFileBacked && ex.SqliteErrorCode is 11 or 26)
        {
            // SQLITE_CORRUPT / SQLITE_NOTADB: keep the damaged file for recovery, start a fresh queue.
            // Never on other errors (e.g. a locked file) — that would sideline a healthy database.
            _conn.Dispose();
            SqliteConnection.ClearAllPools();
            var preserved = $"{databasePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(databasePath, preserved);
            _logger.LogError(ex,
                "Activity buffer database was unreadable (SQLite {Code}); preserved as {Preserved} and started a new one",
                ex.SqliteErrorCode, preserved);
            _conn = OpenConnection(databasePath, isUri);
            InitializeSchema();
        }
    }

    private static SqliteConnection OpenConnection(string databasePath, bool isUri)
    {
        var conn = isUri
            ? new SqliteConnection($"Data Source={databasePath}")
            : new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = databasePath == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate
            }.ToString());
        conn.Open();
        return conn;
    }

    private bool IsFileBacked =>
        !_databasePath.Contains("mode=memory", StringComparison.OrdinalIgnoreCase)
        && _databasePath != ":memory:";

    /// <summary>Isolated in-memory DB (unique per call).</summary>
    public static ActivityRecordBuffer CreateInMemory(int maxRecords = 5_000, int? reservedRecords = null)
    {
        var name = $"file:onevo_mem_{Guid.NewGuid():N}?mode=memory&cache=shared";
        return new ActivityRecordBuffer(name, maxRecords, DefaultMaxDatabaseBytes, reservedRecords);
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

            CREATE TABLE IF NOT EXISTS evidence_spool (
                event_id        TEXT PRIMARY KEY,
                encrypted_path  TEXT NULL,
                encrypted_size  INTEGER NOT NULL,
                created_at      TEXT NOT NULL,
                expires_at      TEXT NOT NULL,
                FOREIGN KEY(event_id) REFERENCES collection_records(event_id) ON DELETE CASCADE
            );

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
        EnsureColumnUnlocked("session_history", "accumulated_idle_sec", "REAL NOT NULL DEFAULT 0");
        // AGT-01: quarantine keeps the original payload; why/when live in their own columns.
        EnsureColumnUnlocked("collection_records", "quarantine_reason", "TEXT NULL");
        EnsureColumnUnlocked("collection_records", "quarantined_at", "TEXT NULL");
        QuarantineLegacyScreenshotRecordsUnlocked();
    }

    private void EnsureColumnUnlocked(string table, string column, string declaration)
    {
        using var info = _conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alter = _conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }

    private void QuarantineLegacyScreenshotRecordsUnlocked()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE collection_records
            SET status = 'quarantined',
                payload_json = $payload
            WHERE record_type = 'screenshot'
              AND status = 'pending';
            """;
        cmd.Parameters.AddWithValue(
            "$payload",
            JsonSerializer.Serialize(new { reason = "legacy_unapproved_payload" }));
        cmd.ExecuteNonQuery();
    }

    public bool HasPendingOrSyncedAttempt(Guid attemptId)
    {
        var eventId = attemptId.ToString("N");
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT COUNT(*)
                FROM collection_records
                WHERE event_id = $eventId
                  AND record_type = $type
                  AND status IN ('pending', 'synced');
                """;
            cmd.Parameters.AddWithValue("$eventId", eventId);
            cmd.Parameters.AddWithValue("$type", CollectionRecordTypes.InactivityCaptureAttempt);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }

    public bool TryEnqueueInactivityAttempt(
        InactivityCaptureAttemptPayload attempt,
        string deviceId,
        string? encryptedSpoolPath,
        int encryptedSize,
        DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            if (!HasRoomUnlocked(CollectionRecordTypes.InactivityCaptureAttempt))
                return false;

            var eventId = attempt.AttemptId.ToString("N");
            using var tx = _conn.BeginTransaction();

            using (var exists = _conn.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM collection_records
                    WHERE event_id = $eventId;
                    """;
                exists.Parameters.AddWithValue("$eventId", eventId);
                if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                {
                    tx.Commit();
                    return true;
                }
            }

            var captureTs = attempt.CapturedAt ?? attempt.PromptedAt;
            var payloadJson = JsonSerializer.Serialize(attempt);
            var now = DateTimeOffset.UtcNow.ToString("O");

            using (var insert = _conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText =
                    """
                    INSERT INTO collection_records
                        (event_id, record_type, schema_version, capture_ts, device_id, payload_json, created_at, status)
                    VALUES
                        ($eventId, $type, $schema, $capture, $device, $payload, $created, 'pending');
                    """;
                insert.Parameters.AddWithValue("$eventId", eventId);
                insert.Parameters.AddWithValue("$type", CollectionRecordTypes.InactivityCaptureAttempt);
                insert.Parameters.AddWithValue("$schema", CollectionSchemaVersions.InactivityCaptureAttemptV1);
                insert.Parameters.AddWithValue("$capture", captureTs.ToUniversalTime().ToString("O"));
                insert.Parameters.AddWithValue("$device", deviceId);
                insert.Parameters.AddWithValue("$payload", payloadJson);
                insert.Parameters.AddWithValue("$created", now);
                insert.ExecuteNonQuery();
            }

            using (var spool = _conn.CreateCommand())
            {
                spool.Transaction = tx;
                spool.CommandText =
                    """
                    INSERT INTO evidence_spool
                        (event_id, encrypted_path, encrypted_size, created_at, expires_at)
                    VALUES
                        ($eventId, $path, $size, $created, $expires);
                    """;
                spool.Parameters.AddWithValue("$eventId", eventId);
                spool.Parameters.AddWithValue("$path", (object?)encryptedSpoolPath ?? DBNull.Value);
                spool.Parameters.AddWithValue("$size", encryptedSize);
                spool.Parameters.AddWithValue("$created", now);
                spool.Parameters.AddWithValue("$expires", expiresAt.ToUniversalTime().ToString("O"));
                spool.ExecuteNonQuery();
            }

            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                RecordStorageFailureUnlocked(ex);
                return false;
            }

            RecordStorageSuccessUnlocked();
            return true;
        }
    }

    public bool TryEnqueuePeriodicScreenshot(
        Guid captureId,
        DateTimeOffset capturedAt,
        string deviceId,
        string encryptedSpoolPath,
        int encryptedSize,
        DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            if (!HasRoomUnlocked(CollectionRecordTypes.Screenshot))
                return false;

            var eventId = captureId.ToString("N");
            using var tx = _conn.BeginTransaction();

            using (var exists = _conn.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText = "SELECT COUNT(*) FROM collection_records WHERE event_id = $eventId;";
                exists.Parameters.AddWithValue("$eventId", eventId);
                if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                {
                    tx.Commit();
                    return true;
                }
            }

            var payloadJson = JsonSerializer.Serialize(new ScreenshotPayload
            {
                CapturedAt = capturedAt,
                Format = "jpeg",
                Data = ""
            });
            var now = DateTimeOffset.UtcNow.ToString("O");

            using (var insert = _conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText =
                    """
                    INSERT INTO collection_records
                        (event_id, record_type, schema_version, capture_ts, device_id, payload_json, created_at, status)
                    VALUES
                        ($eventId, $type, $schema, $capture, $device, $payload, $created, 'pending');
                    """;
                insert.Parameters.AddWithValue("$eventId", eventId);
                insert.Parameters.AddWithValue("$type", CollectionRecordTypes.Screenshot);
                insert.Parameters.AddWithValue("$schema", CollectionSchemaVersions.ScreenshotV1);
                insert.Parameters.AddWithValue("$capture", capturedAt.ToUniversalTime().ToString("O"));
                insert.Parameters.AddWithValue("$device", deviceId);
                insert.Parameters.AddWithValue("$payload", payloadJson);
                insert.Parameters.AddWithValue("$created", now);
                insert.ExecuteNonQuery();
            }

            using (var spool = _conn.CreateCommand())
            {
                spool.Transaction = tx;
                spool.CommandText =
                    """
                    INSERT INTO evidence_spool
                        (event_id, encrypted_path, encrypted_size, created_at, expires_at)
                    VALUES
                        ($eventId, $path, $size, $created, $expires);
                    """;
                spool.Parameters.AddWithValue("$eventId", eventId);
                spool.Parameters.AddWithValue("$path", encryptedSpoolPath);
                spool.Parameters.AddWithValue("$size", encryptedSize);
                spool.Parameters.AddWithValue("$created", now);
                spool.Parameters.AddWithValue("$expires", expiresAt.ToUniversalTime().ToString("O"));
                spool.ExecuteNonQuery();
            }

            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                RecordStorageFailureUnlocked(ex);
                return false;
            }

            RecordStorageSuccessUnlocked();
            return true;
        }
    }

    public List<BufferedCollectionRecord> PeekPendingBatch(int maxCount)
    {
        lock (_gate)
        {
            var batch = new List<BufferedCollectionRecord>();

            using var select = _conn.CreateCommand();
            select.CommandText =
                """
                SELECT id, event_id, record_type, schema_version, capture_ts, device_id, payload_json
                FROM collection_records
                WHERE status = 'pending'
                ORDER BY id ASC
                LIMIT $limit;
                """;
            select.Parameters.AddWithValue("$limit", Math.Max(1, maxCount));

            var corrupt = new List<long>();
            using (var reader = select.ExecuteReader())
            while (reader.Read())
            {
                var payloadJson = reader.GetString(6);
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(payloadJson);
                }
                catch (JsonException)
                {
                    // One unreadable row must not fail every flush behind it.
                    corrupt.Add(reader.GetInt64(0));
                    continue;
                }

                using var _ = doc;
                batch.Add(new BufferedCollectionRecord(
                    reader.GetInt64(0),
                    new CollectionRecord
                    {
                        EventId = reader.GetString(1),
                        RecordType = reader.GetString(2),
                        SchemaVersion = reader.GetString(3),
                        CaptureTimestamp = DateTimeOffset.Parse(reader.GetString(4)),
                        DeviceId = reader.GetString(5),
                        Payload = doc.RootElement.Clone()
                    }));
            }

            foreach (var rowId in corrupt)
                QuarantineRowUnlocked(rowId, QuarantineReasons.CorruptPayload);
            return batch;
        }
    }

    public void MarkAcknowledged(IEnumerable<long> rowIds)
    {
        var ids = rowIds.Distinct().ToList();
        if (ids.Count == 0) return;

        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                $"UPDATE collection_records SET status = 'synced' WHERE id IN ({string.Join(",", ids)}) AND status = 'pending';";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Moves a pending row out of the send queue without touching its payload, so it can be
    /// exported or requeued later (<see cref="RequeueQuarantined"/>).
    /// </summary>
    public void QuarantineRow(long rowId, string? reason = null)
    {
        lock (_gate)
        {
            QuarantineRowUnlocked(rowId, reason ?? QuarantineReasons.Unspecified);
        }
    }

    private void QuarantineRowUnlocked(long rowId, string reason)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE collection_records
            SET status = 'quarantined',
                quarantine_reason = $reason,
                quarantined_at = $at
            WHERE id = $id AND status = 'pending';
            """;
        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public int QuarantinedCount
    {
        get
        {
            lock (_gate)
            {
                return CountQuarantinedUnlocked();
            }
        }
    }

    /// <summary>
    /// Puts quarantined rows back in the send queue (keeping their original order). Optionally only
    /// rows whose reason starts with <paramref name="reasonPrefix"/> and that were quarantined before
    /// <paramref name="quarantinedBefore"/>. Legacy rows whose payload was replaced are never requeued.
    /// </summary>
    public int RequeueQuarantined(string? reasonPrefix = null, DateTimeOffset? quarantinedBefore = null)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                UPDATE collection_records
                SET status = 'pending', quarantine_reason = NULL, quarantined_at = NULL
                WHERE status = 'quarantined'
                  AND quarantine_reason IS NOT NULL
                  AND ($prefix IS NULL OR quarantine_reason LIKE $prefix || '%')
                  AND ($before IS NULL OR quarantined_at < $before);
                """;
            cmd.Parameters.AddWithValue("$prefix", (object?)reasonPrefix ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$before", (object?)quarantinedBefore?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Writes every quarantined row as one JSON object per line (recovery / support export).</summary>
    public int ExportQuarantined(TextWriter writer)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT id, event_id, record_type, schema_version, capture_ts, created_at,
                       quarantine_reason, quarantined_at, payload_json
                FROM collection_records
                WHERE status = 'quarantined'
                ORDER BY id ASC;
                """;
            using var reader = cmd.ExecuteReader();
            var count = 0;
            while (reader.Read())
            {
                writer.WriteLine(JsonSerializer.Serialize(new
                {
                    row_id = reader.GetInt64(0),
                    event_id = reader.GetString(1),
                    record_type = reader.GetString(2),
                    schema_version = reader.GetString(3),
                    capture_ts = reader.GetString(4),
                    created_at = reader.GetString(5),
                    quarantine_reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                    quarantined_at = reader.IsDBNull(7) ? null : reader.GetString(7),
                    payload = reader.GetString(8)
                }));
                count++;
            }

            return count;
        }
    }

    public void ScheduleRetry(IEnumerable<long> rowIds)
    {
        // Pending rows stay pending until explicitly acknowledged. This hook preserves
        // the event id for future retry/backoff orchestration (Task 10).
        _ = rowIds;
    }

    public EvidenceSpoolEntry? GetEvidenceSpoolEntry(string eventId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT event_id, encrypted_path, encrypted_size, created_at, expires_at
                FROM evidence_spool
                WHERE event_id = $eventId;
                """;
            cmd.Parameters.AddWithValue("$eventId", eventId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new EvidenceSpoolEntry(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4)));
        }
    }

    public void DeleteEvidenceSpoolEntry(string eventId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM evidence_spool WHERE event_id = $eventId;";
            cmd.Parameters.AddWithValue("$eventId", eventId);
            cmd.ExecuteNonQuery();
        }
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
            if (!HasRoomUnlocked(record.RecordType))
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
            try
            {
                var ok = cmd.ExecuteNonQuery() > 0;
                RecordStorageSuccessUnlocked();
                return ok;
            }
            catch (SqliteException ex)
            {
                // Disk full, I/O error, read-only file: nothing is deleted to make it fit.
                RecordStorageFailureUnlocked(ex);
                return false;
            }
        }
    }

    /// <summary>
    /// AGT-02 capacity policy (architecture §8: never silently delete identity or lifecycle records).
    /// <list type="number">
    /// <item>Low-priority snapshots are accepted up to <c>maxRecords</c> pending rows; lifecycle/evidence
    /// records up to <c>maxRecords + reservedRecords</c>.</item>
    /// <item>At the limit, pending snapshots older than <see cref="MaxSnapshotAge"/> (the backend refuses
    /// them anyway) are quarantined — kept, not deleted — to make room.</item>
    /// <item>Over the byte limit, already-delivered rows are purged (synced inactivity attempts are kept
    /// for dedupe).</item>
    /// <item>Otherwise the new record is refused and the refusal is reported (health = Full). Pending and
    /// quarantined rows are never deleted to make room.</item>
    /// </list>
    /// </summary>
    private bool HasRoomUnlocked(string recordType)
    {
        var limit = IsReservedType(recordType) ? _maxRecords + _reservedRecords : _maxRecords;
        if (CountUnlocked() >= limit)
        {
            var cutoff = (DateTimeOffset.UtcNow - MaxSnapshotAge).ToString("O");
            var stale = ExecuteUnlocked(
                $"""
                UPDATE collection_records
                SET status = 'quarantined', quarantine_reason = $reason, quarantined_at = $at
                WHERE status = 'pending'
                  AND record_type IN {LowPriorityTypesSql}
                  AND capture_ts < $cutoff;
                """,
                ("$reason", QuarantineReasons.StaleOver24h),
                ("$at", DateTimeOffset.UtcNow.ToString("O")),
                ("$cutoff", cutoff));
            if (stale > 0)
                _logger.LogWarning(
                    "Activity buffer at capacity — quarantined {Count} pending snapshot(s) older than {Hours}h (kept for export)",
                    stale, MaxSnapshotAge.TotalHours);

            if (CountUnlocked() >= limit)
                return RefuseUnlocked(recordType, "record limit");
        }

        if (DatabaseBytesUnlocked() >= _maxDatabaseBytes)
        {
            var purged = ExecuteUnlocked(
                $"""
                DELETE FROM collection_records
                WHERE status = 'synced'
                  AND record_type <> '{CollectionRecordTypes.InactivityCaptureAttempt}';
                """);
            if (purged > 0)
            {
                ExecuteUnlocked("VACUUM;");
                _logger.LogInformation(
                    "Activity buffer at size limit — purged {Count} already-delivered row(s)", purged);
            }

            if (DatabaseBytesUnlocked() >= _maxDatabaseBytes)
                return RefuseUnlocked(recordType, "size limit");
        }

        LogHealthTransitionUnlocked();
        return true;
    }

    private static bool IsReservedType(string recordType) =>
        recordType is CollectionRecordTypes.WorkSession
            or CollectionRecordTypes.InactivityCaptureAttempt
            or CollectionRecordTypes.FacePhoto;

    private bool RefuseUnlocked(string recordType, string limit)
    {
        LogHealthTransitionUnlocked();
        _logger.LogWarning(
            "Activity buffer full ({Limit}) — new {RecordType} not captured; queued rows are kept. Pending={Pending} Quarantined={Quarantined}",
            limit, recordType, CountUnlocked(), CountQuarantinedUnlocked());
        return false;
    }

    private void RecordStorageFailureUnlocked(Exception ex)
    {
        _storageFailed = true;
        _logger.LogError(ex, "Activity buffer write failed — record not captured; existing rows untouched");
        LogHealthTransitionUnlocked();
    }

    private void RecordStorageSuccessUnlocked()
    {
        if (!_storageFailed)
            return;
        _storageFailed = false;
        LogHealthTransitionUnlocked();
    }

    /// <summary>Current queue state for status reporting (IPC) and logs.</summary>
    public BufferHealth GetHealth()
    {
        lock (_gate)
        {
            return GetHealthUnlocked();
        }
    }

    private BufferHealth GetHealthUnlocked()
    {
        var pending = CountUnlocked();
        var bytes = DatabaseBytesUnlocked();
        var level = _storageFailed
            ? BufferHealthLevel.StorageError
            : pending >= _maxRecords || bytes >= _maxDatabaseBytes
                ? BufferHealthLevel.Full
                : pending * 100L >= _maxRecords * (long)WarningPercent || bytes * 100 >= _maxDatabaseBytes * WarningPercent
                    ? BufferHealthLevel.Warning
                    : BufferHealthLevel.Ok;
        return new BufferHealth(
            level, pending, CountQuarantinedUnlocked(), bytes, _maxRecords, _maxRecords + _reservedRecords, _maxDatabaseBytes);
    }

    private void LogHealthTransitionUnlocked()
    {
        var health = GetHealthUnlocked();
        if (health.Level == _lastLoggedLevel)
            return;

        var level = health.Level is BufferHealthLevel.Ok ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level,
            "Activity buffer health {From} -> {To}: Pending={Pending}/{Limit} Quarantined={Quarantined} Bytes={Bytes}/{MaxBytes}",
            _lastLoggedLevel, health.Level, health.Pending, health.LowPriorityLimit, health.Quarantined,
            health.DatabaseBytes, health.MaxDatabaseBytes);
        _lastLoggedLevel = health.Level;
    }

    private long DatabaseBytesUnlocked()
    {
        if (!IsFileBacked)
            return 0;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private int CountQuarantinedUnlocked()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM collection_records WHERE status = 'quarantined';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int ExecuteUnlocked(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Test hook: caps the database so the next write fails with SQLITE_FULL.</summary>
    internal void SetMaxPageCountForTesting(long pages)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"PRAGMA max_page_count = {pages};";
            cmd.ExecuteNonQuery();
        }
    }

    [Obsolete("Use PeekPendingBatch + MarkAcknowledged so rows stay pending until HTTP ack.")]
    public List<CollectionRecord> DequeueBatch(int maxCount) =>
        PeekPendingBatch(maxCount).Select(p => p.Record).ToList();

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
        string? scheduleDisplay,
        TimeSpan accumulatedIdle)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO session_history
                    (clock_in_at, clock_out_at, accumulated_break_sec, accumulated_work_sec,
                     break_session_count, schedule_display, created_at, accumulated_idle_sec)
                VALUES
                    ($cin, $cout, $brk, $work, $breaks, $sched, $created, $idle);
                """;
            cmd.Parameters.AddWithValue("$cin", (object?)clockIn?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cout", (object?)clockOut?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brk", accumulatedBreak.TotalSeconds);
            cmd.Parameters.AddWithValue("$work", accumulatedWork.TotalSeconds);
            cmd.Parameters.AddWithValue("$breaks", breakSessionCount);
            cmd.Parameters.AddWithValue("$sched", (object?)scheduleDisplay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$idle", accumulatedIdle.TotalSeconds);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>The most recently finished session saved by <see cref="SaveSessionHistory"/>, or null.</summary>
    public SessionHistoryEntry? GetLatestCompletedSession()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT clock_in_at, clock_out_at, accumulated_break_sec, accumulated_work_sec,
                       break_session_count, schedule_display, accumulated_idle_sec
                FROM session_history
                WHERE clock_in_at IS NOT NULL AND clock_out_at IS NOT NULL
                ORDER BY id DESC
                LIMIT 1;
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new SessionHistoryEntry(
                DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(reader.GetDouble(2)),
                TimeSpan.FromSeconds(reader.GetDouble(3)),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                TimeSpan.FromSeconds(reader.GetDouble(6)));
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

public sealed record SessionHistoryEntry(
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    TimeSpan AccumulatedBreak,
    TimeSpan AccumulatedWork,
    int BreakSessionCount,
    string? ScheduleDisplay,
    TimeSpan AccumulatedIdle);
