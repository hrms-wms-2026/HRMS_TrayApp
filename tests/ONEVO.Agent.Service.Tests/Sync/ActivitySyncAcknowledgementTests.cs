using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Sync;

/// <summary>
/// AGT-01: a buffered row leaves the queue only when the backend acknowledged it (stored or already
/// stored) or permanently refused it — and a refused row is quarantined with its payload intact,
/// never silently marked delivered.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class ActivitySyncAcknowledgementTests
{
    private static CollectionRecord Activity(DateTimeOffset capturedAt) => new CollectionRecord
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.ActivitySnapshot,
        SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
        CaptureTimestamp = capturedAt,
        DeviceId = "test",
        Payload = JsonSerializer.SerializeToElement(new ActivitySnapshotPayload
        {
            CapturedAt = capturedAt,
            KeyboardEventsCount = 3,
            MouseEventsCount = 4,
            ActiveSeconds = 30,
            IdleSeconds = 0,
            IntensityScore = 10,
            ForegroundProcessName = "code.exe"
        })
    };

    private static CollectionRecord WorkSession() => new CollectionRecord
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordType = CollectionRecordTypes.WorkSession,
        SchemaVersion = "1.0",
        CaptureTimestamp = DateTimeOffset.UtcNow,
        DeviceId = "test",
        Payload = JsonSerializer.SerializeToElement(new WorkSessionPayload
        {
            SessionId = Guid.NewGuid(),
            ClockInAt = DateTimeOffset.UtcNow.AddHours(-8),
            ClockOutAt = DateTimeOffset.UtcNow,
            AccumulatedBreak = TimeSpan.FromMinutes(30),
            AccumulatedWork = TimeSpan.FromHours(7.5),
            BreakSessionCount = 1
        })
    };

    private static ActivitySyncService Build(ActivityRecordBuffer buffer, IHttpClientFactory factory, CredentialStore credentials) =>
        new(
            NullLogger<ActivitySyncService>.Instance,
            buffer,
            credentials,
            factory,
            new PassthroughEvidenceProtector(),
            new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
            Options.Create(new AgentOptions { ApiBaseUrl = "https://api.example.com" }));

    private static void Flush(ActivityRecordBuffer buffer, IHttpClientFactory factory)
    {
        var credentials = new CredentialStore();
        try
        {
            credentials.StoreDeviceJwt("test-jwt");
            Build(buffer, factory, credentials).FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    private static List<JsonElement> Quarantined(ActivityRecordBuffer buffer)
    {
        using var writer = new StringWriter();
        buffer.ExportQuarantined(writer);
        return writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private static List<DateTimeOffset> SentCapturedAts(HttpRequestMessage req)
    {
        var json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<ActivityIngestRequest>(json)!.Snapshots.Select(s => s.CapturedAt).ToList();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    [Fact]
    public void All_records_older_than_24h_are_quarantined_with_payload_and_never_sent()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddHours(-30)));
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddHours(-26)));
        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        Flush(buffer, factory);

        Assert.Empty(factory.Requests);
        Assert.Equal(0, buffer.Count);
        var rows = Quarantined(buffer);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(QuarantineReasons.StaleOver24h, row.GetProperty("quarantine_reason").GetString());
            Assert.Contains("KeyboardEventsCount", row.GetProperty("payload").GetString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Partial_success_quarantines_only_the_items_the_server_rejected()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var kept = DateTimeOffset.UtcNow.AddMinutes(-3);
        var refused = DateTimeOffset.UtcNow.AddMinutes(-2);
        buffer.TryEnqueue(Activity(kept));
        buffer.TryEnqueue(Activity(refused));
        // The server truncates to microseconds when it reports a rejection.
        var reported = new DateTimeOffset(refused.UtcTicks - refused.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        var factory = new CapturingHttpClientFactory(_ => Json(HttpStatusCode.Accepted, new
        {
            stored = 1,
            duplicates = 0,
            rejected = new[] { new { captured_at = reported, code = "monitoring.snapshot_too_old" } }
        }));

        Flush(buffer, factory);

        Assert.Equal(0, buffer.Count);
        var row = Assert.Single(Quarantined(buffer));
        Assert.Equal("rejected:202:monitoring.snapshot_too_old", row.GetProperty("quarantine_reason").GetString());
        Assert.Equal(1, buffer.TotalStoredCount - buffer.QuarantinedCount);
    }

    [Fact]
    public void Validation_400_on_a_batch_is_isolated_so_valid_neighbours_are_still_stored()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var good1 = DateTimeOffset.UtcNow.AddMinutes(-3);
        var bad = DateTimeOffset.UtcNow.AddMinutes(-2);
        var good2 = DateTimeOffset.UtcNow.AddMinutes(-1);
        buffer.TryEnqueue(Activity(good1));
        buffer.TryEnqueue(Activity(bad));
        buffer.TryEnqueue(Activity(good2));
        var factory = new CapturingHttpClientFactory(req =>
            SentCapturedAts(req).Contains(bad)
                ? Json(HttpStatusCode.BadRequest, new { detail = "intensity_score out of range" })
                : new HttpResponseMessage(HttpStatusCode.Accepted));

        Flush(buffer, factory);

        Assert.Equal(4, factory.Requests.Count); // the batch, then one request per record
        Assert.Equal(0, buffer.Count);
        var row = Assert.Single(Quarantined(buffer));
        Assert.Equal("rejected:400", row.GetProperty("quarantine_reason").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void Transient_or_auth_failures_keep_every_record_pending(HttpStatusCode status)
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-2)));
        buffer.TryEnqueue(WorkSession());
        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(status));

        Flush(buffer, factory);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(0, buffer.QuarantinedCount);
    }

    [Fact]
    public void Capability_disabled_403_quarantines_with_reason_instead_of_dropping()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-2)));
        var factory = new CapturingHttpClientFactory(_ => Json(HttpStatusCode.Forbidden, new
        {
            detail = "Activity monitoring is not enabled for this employee.",
            code = "monitoring.activity_monitoring_disabled"
        }));

        Flush(buffer, factory);

        Assert.Equal(0, buffer.Count);
        Assert.Equal(QuarantineReasons.PolicyDisabled, Assert.Single(Quarantined(buffer)).GetProperty("quarantine_reason").GetString());
    }

    [Fact]
    public void Network_failure_after_server_commit_keeps_rows_pending_and_replay_is_acknowledged()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-2)));
        var lost = new CapturingHttpClientFactory(_ => throw new HttpRequestException("connection reset after commit"));

        Flush(buffer, lost);
        Assert.Equal(1, buffer.Count);

        // Replay: the server already has it and says so.
        var replay = new CapturingHttpClientFactory(_ => Json(HttpStatusCode.Accepted, new { stored = 0, duplicates = 1, rejected = Array.Empty<object>() }));
        Flush(buffer, replay);

        Assert.Single(replay.Requests);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.QuarantinedCount);
    }

    [Fact]
    public void Work_session_rejected_with_400_is_quarantined_not_dropped()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(WorkSession());
        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        Flush(buffer, factory);

        Assert.Equal(0, buffer.Count);
        Assert.Equal("work_session", Assert.Single(Quarantined(buffer)).GetProperty("record_type").GetString());
    }

    [Fact]
    public void Corrupt_row_is_quarantined_and_does_not_block_the_rest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo_ack_{Guid.NewGuid():N}.db");
        try
        {
            using (var seed = new ActivityRecordBuffer(path))
            {
                seed.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-3)));
                seed.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-2)));
            }

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE collection_records SET payload_json = '{not json' WHERE id = 1;";
                cmd.ExecuteNonQuery();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            using var buffer = new ActivityRecordBuffer(path);
            var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
            Flush(buffer, factory);

            Assert.Single(factory.Requests);
            Assert.Equal(0, buffer.Count);
            Assert.Equal(QuarantineReasons.CorruptPayload, Assert.Single(Quarantined(buffer)).GetProperty("quarantine_reason").GetString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void Pending_rows_survive_an_agent_restart_and_are_sent_afterwards()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onevo_restart_{Guid.NewGuid():N}.db");
        try
        {
            using (var beforeCrash = new ActivityRecordBuffer(path))
            {
                beforeCrash.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-2)));
                Flush(beforeCrash, new CapturingHttpClientFactory(_ => throw new HttpRequestException("offline")));
                Assert.Equal(1, beforeCrash.Count);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            using var afterRestart = new ActivityRecordBuffer(path);
            Assert.Equal(1, afterRestart.Count);
            var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
            Flush(afterRestart, factory);

            Assert.Single(factory.Requests);
            Assert.Equal(0, afterRestart.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void One_flush_drains_several_batches_but_stays_bounded()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory(maxRecords: 5_000);
        var start = DateTimeOffset.UtcNow.AddHours(-20);
        var total = ActivitySyncService.BatchSize * (ActivitySyncService.MaxBatchesPerFlush + 2);
        for (var i = 0; i < total; i++)
            buffer.TryEnqueue(Activity(start.AddSeconds(i)));
        var factory = new CapturingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        Flush(buffer, factory);

        Assert.Equal(ActivitySyncService.MaxBatchesPerFlush, factory.Requests.Count);
        Assert.Equal(ActivitySyncService.BatchSize * 2, buffer.Count);
    }

    [Fact]
    public void Future_time_rejections_are_requeued_automatically_after_the_skew_window()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        buffer.TryEnqueue(Activity(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var row = buffer.PeekPendingBatch(1)[0];
        buffer.QuarantineRow(row.RowId, QuarantineReasons.FutureTime);

        Assert.Equal(0, buffer.RequeueQuarantined(QuarantineReasons.FutureTime, DateTimeOffset.UtcNow.AddMinutes(-10)));
        Assert.Equal(1, buffer.RequeueQuarantined(QuarantineReasons.FutureTime, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Equal(1, buffer.Count);
    }
}
