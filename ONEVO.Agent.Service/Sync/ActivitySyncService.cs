namespace ONEVO.Agent.Service.Sync;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Flushes buffered collection records to backend ingest endpoints.
/// Uses Device JWT from CredentialStore — never trusts tenant_id in payload.
/// </summary>
public sealed class ActivitySyncService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ActivitySyncService> _logger;
    private readonly ActivityRecordBuffer _buffer;
    private readonly CredentialStore _credentials;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _options;

    public ActivitySyncService(
        ILogger<ActivitySyncService> logger,
        ActivityRecordBuffer buffer,
        CredentialStore credentials,
        IHttpClientFactory httpClientFactory,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _buffer = buffer;
        _credentials = credentials;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.IngestIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Activity sync flush failed; will retry");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Best-effort final flush on stop
        try { await FlushAsync(CancellationToken.None); }
        catch { /* ignore */ }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var batch = _buffer.DequeueBatch(maxCount: 100);
        if (batch.Count == 0)
            return;

        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("No device JWT — re-queuing {Count} records", batch.Count);
            _buffer.RequeueFront(batch);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl not configured — re-queuing records");
            _buffer.RequeueFront(batch);
            return;
        }

        var requeue = new List<CollectionRecord>();

        requeue.AddRange(await FlushActivitySnapshotsAsync(
            batch.Where(r => r.RecordType == CollectionRecordTypes.ActivitySnapshot).ToList(),
            jwt, ct));

        requeue.AddRange(await FlushAppUsageSnapshotsAsync(
            batch.Where(r => r.RecordType == CollectionRecordTypes.AppUsageSnapshot).ToList(),
            jwt, ct));

        requeue.AddRange(await FlushDeviceStateSnapshotsAsync(
            batch.Where(r => r.RecordType == CollectionRecordTypes.DeviceStateSnapshot).ToList(),
            jwt, ct));

        requeue.AddRange(await FlushWorkSessionsAsync(
            batch.Where(r => r.RecordType == CollectionRecordTypes.WorkSession).ToList(),
            jwt, ct));

        if (requeue.Count > 0)
            _buffer.RequeueFront(requeue);
    }

    private async Task<List<CollectionRecord>> FlushActivitySnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var items = new List<ActivityIngestItem>();
        var used  = new List<CollectionRecord>();

        foreach (var record in records)
        {
            try
            {
                var snap = record.Payload.Deserialize<ActivitySnapshotPayload>(JsonOptions);
                if (snap is null) continue;

                items.Add(new ActivityIngestItem
                {
                    CapturedAt           = snap.CapturedAt,
                    KeyboardEventsCount  = snap.KeyboardEventsCount,
                    MouseEventsCount     = snap.MouseEventsCount,
                    ActiveSeconds        = snap.ActiveSeconds,
                    IdleSeconds          = snap.IdleSeconds,
                    IntensityScore       = snap.IntensityScore,
                    ForegroundProcessName = snap.ForegroundProcessName
                });
                used.Add(record);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Corrupt activity record quarantined eventId={EventId}", record.EventId);
            }
        }

        if (items.Count == 0) return [];
        return await PostBatchAsync(
            AgentApiRoutes.ActivitySnapshots, jwt,
            new ActivityIngestRequest { Snapshots = items },
            used, ct);
    }

    private async Task<List<CollectionRecord>> FlushAppUsageSnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var items = new List<AppUsageIngestItem>();
        var used  = new List<CollectionRecord>();

        foreach (var record in records)
        {
            try
            {
                var snap = record.Payload.Deserialize<AppUsageSnapshotPayload>(JsonOptions);
                if (snap is null) continue;

                items.Add(new AppUsageIngestItem
                {
                    CapturedAt      = snap.CapturedAt,
                    ProcessName     = snap.ProcessName,
                    WindowTitleHash = snap.WindowTitleHash
                });
                used.Add(record);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Corrupt app-usage record quarantined eventId={EventId}", record.EventId);
            }
        }

        if (items.Count == 0) return [];
        return await PostBatchAsync(
            AgentApiRoutes.AppUsageSnapshots, jwt,
            new AppUsageIngestRequest { Snapshots = items },
            used, ct);
    }

    private async Task<List<CollectionRecord>> FlushDeviceStateSnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var items = new List<DeviceStateIngestItem>();
        var used  = new List<CollectionRecord>();

        foreach (var record in records)
        {
            try
            {
                var snap = record.Payload.Deserialize<DeviceStateSnapshotPayload>(JsonOptions);
                if (snap is null) continue;

                items.Add(new DeviceStateIngestItem
                {
                    CapturedAt  = snap.CapturedAt,
                    IdleSeconds = snap.IdleSeconds,
                    IsIdle      = snap.IsIdle
                });
                used.Add(record);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Corrupt device-state record quarantined eventId={EventId}", record.EventId);
            }
        }

        if (items.Count == 0) return [];
        return await PostBatchAsync(
            AgentApiRoutes.DeviceStateSnapshots, jwt,
            new DeviceStateIngestRequest { Snapshots = items },
            used, ct);
    }

    /// <summary>
    /// One POST per completed session (no batching — sessions are rare compared to
    /// activity snapshots). Each is upserted server-side by SessionId, so a retried
    /// delivery after a dropped response is a no-op, never a duplicate row.
    /// </summary>
    private async Task<List<CollectionRecord>> FlushWorkSessionsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var requeue = new List<CollectionRecord>();
        foreach (var record in records)
        {
            WorkSessionPayload? session;
            try
            {
                session = record.Payload.Deserialize<WorkSessionPayload>(JsonOptions);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Corrupt work-session record quarantined eventId={EventId}", record.EventId);
                continue;
            }

            if (session is null) continue;

            var body = new WorkSessionSubmitRequest
            {
                SessionId = session.SessionId,
                ClockInAt = session.ClockInAt,
                ClockOutAt = session.ClockOutAt,
                AccumulatedBreakSeconds = (int)session.AccumulatedBreak.TotalSeconds,
                AccumulatedWorkSeconds = (int)session.AccumulatedWork.TotalSeconds,
                BreakSessionCount = session.BreakSessionCount,
                ScheduleDisplay = session.ScheduleDisplay
            };

            var failed = await PostBatchAsync(AgentApiRoutes.WorkSessionSubmit, jwt, body, [record], ct);
            requeue.AddRange(failed);
        }

        return requeue;
    }

    private async Task<List<CollectionRecord>> PostBatchAsync(
        string route, string jwt, object body, List<CollectionRecord> records, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP failed for {Route} — re-queue {Count}", route, records.Count);
            return records;
        }

        if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK)
        {
            _logger.LogInformation("Batch accepted for {Route}. Count={Count}", route, records.Count);
            return [];
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Rejected status={Status} for {Route} — dropping pending re-enrollment",
                (int)response.StatusCode, route);
            return [];
        }

        _logger.LogWarning(
            "Non-success status={Status} for {Route} — re-queue {Count}",
            (int)response.StatusCode, route, records.Count);
        return records;
    }
}
