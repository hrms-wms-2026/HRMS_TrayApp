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
/// Flushes buffered activity snapshots to backend ingest endpoint.
/// Uses Device JWT from CredentialStore — never trusts tenant_id in payload.
/// </summary>
public sealed class ActivitySyncService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<ActivitySyncService> _logger;
    private readonly ActivityRecordBuffer _buffer;
    private readonly ICredentialStore _credentials;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AgentOptions _options;

    public ActivitySyncService(
        ILogger<ActivitySyncService> logger,
        ActivityRecordBuffer buffer,
        ICredentialStore credentials,
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

        // Best-effort final flush on stop, bounded so the service stops within the SCM timeout (ISS-021).
        try
        {
            using var finalFlush = new CancellationTokenSource(FinalFlushTimeout);
            await FlushAsync(finalFlush.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final activity flush on stop did not complete; {Count} records remain buffered", _buffer.Count);
        }
    }

    /// <summary>
    /// Uploads one batch. Every dequeued record is either accepted by the backend, explicitly
    /// quarantined (logged), or returned to the front of the buffer — on every path, including
    /// auth rejection and unexpected exceptions (ISS-007, ISS-008).
    /// </summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        var batch = _buffer.DequeueBatch(maxCount: 100);
        if (batch.Count == 0)
            return;

        IReadOnlyList<CollectionRecord> keep = batch;
        try
        {
            keep = await SendAsync(batch, ct);
        }
        finally
        {
            if (keep.Count > 0)
                _buffer.RequeueFront(keep);
        }
    }

    /// <returns>The records that must stay buffered for a later attempt.</returns>
    private async Task<IReadOnlyList<CollectionRecord>> SendAsync(List<CollectionRecord> batch, CancellationToken ct)
    {
        var records = new List<CollectionRecord>(batch.Count);
        var items = new List<ActivityIngestItem>(batch.Count);
        foreach (var record in batch)
        {
            if (!TryMap(record, out var item))
                continue;
            records.Add(record);
            items.Add(item);
        }

        if (items.Count == 0)
            return []; // every record was quarantined

        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("No device JWT — keeping {Count} activity records", records.Count);
            return records;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl not configured — keeping {Count} activity records", records.Count);
            return records;
        }

        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.ActivitySnapshots)
        {
            Content = JsonContent.Create(new ActivityIngestRequest { Snapshots = items })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Activity ingest HTTP failed — keeping {Count} records", records.Count);
            return records;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK)
            {
                _logger.LogInformation("Activity batch accepted by backend. Count={Count}", items.Count);
                return [];
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Activity ingest rejected status={Status} — keeping {Count} records until the device credential is renewed",
                    (int)response.StatusCode, records.Count);
                return records;
            }

            _logger.LogWarning(
                "Activity ingest non-success status={Status} — keeping {Count} records",
                (int)response.StatusCode, records.Count);
            return records;
        }
    }

    private bool TryMap(CollectionRecord record, out ActivityIngestItem item)
    {
        item = null!;
        if (record.RecordType != CollectionRecordTypes.ActivitySnapshot)
        {
            _logger.LogWarning("Unsupported record type quarantined eventId={EventId} type={Type}", record.EventId, record.RecordType);
            return false;
        }

        ActivitySnapshotPayload? snap;
        try
        {
            snap = record.Payload.Deserialize<ActivitySnapshotPayload>(JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Corrupt activity record quarantined eventId={EventId}", record.EventId);
            return false;
        }

        if (snap is null)
        {
            _logger.LogWarning("Empty activity record quarantined eventId={EventId}", record.EventId);
            return false;
        }

        item = new ActivityIngestItem
        {
            CapturedAt = snap.CapturedAt,
            KeyboardEventsCount = snap.KeyboardEventsCount,
            MouseEventsCount = snap.MouseEventsCount,
            ActiveSeconds = snap.ActiveSeconds,
            IdleSeconds = snap.IdleSeconds,
            IntensityScore = snap.IntensityScore,
            ForegroundProcessName = snap.ForegroundProcessName
        };
        return true;
    }
}
