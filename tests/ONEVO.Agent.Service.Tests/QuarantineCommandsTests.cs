using System.Text.Json;
using Microsoft.Data.Sqlite;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

public class QuarantineCommandsTests
{
    [Fact]
    public void Export_then_requeue_recovers_quarantined_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"onevo_qcmd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "agent_activity.db");
        var export = Path.Combine(dir, "quarantine.jsonl");
        try
        {
            using (var buffer = new ActivityRecordBuffer(db))
            {
                buffer.TryEnqueue(new CollectionRecord
                {
                    EventId = "e1",
                    RecordType = CollectionRecordTypes.ActivitySnapshot,
                    SchemaVersion = "1.0",
                    CaptureTimestamp = DateTimeOffset.UtcNow,
                    DeviceId = "d",
                    Payload = JsonSerializer.SerializeToElement(new { ok = true })
                });
                buffer.QuarantineRow(buffer.PeekPendingBatch(1)[0].RowId, QuarantineReasons.Rejected(400, null));
            }
            SqliteConnection.ClearAllPools();

            var output = new StringWriter();
            Assert.Equal(0, QuarantineCommands.Run([QuarantineCommands.Export, export, "--db", db], output));
            var line = Assert.Single(File.ReadAllLines(export));
            Assert.Contains("\"event_id\":\"e1\"", line, StringComparison.Ordinal);

            Assert.Equal(0, QuarantineCommands.Run([QuarantineCommands.Requeue, "rejected:", "--db", db], output));
            SqliteConnection.ClearAllPools();
            using var reopened = new ActivityRecordBuffer(db);
            Assert.Equal(1, reopened.Count);
            Assert.Equal(0, reopened.QuarantinedCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_database_is_reported_not_created()
    {
        var db = Path.Combine(Path.GetTempPath(), $"onevo_missing_{Guid.NewGuid():N}.db");
        var output = new StringWriter();
        Assert.Equal(2, QuarantineCommands.Run([QuarantineCommands.Requeue, "--db", db], output));
        Assert.False(File.Exists(db));
    }
}
