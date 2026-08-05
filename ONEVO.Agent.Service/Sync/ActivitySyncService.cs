namespace ONEVO.Agent.Service.Sync;

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
            _logger.LogDebug("No device JWT — re-queuing {Count} activity records", batch.Count);
            _buffer.RequeueFront(batch);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl not configured — re-queuing activity records");
            _buffer.RequeueFront(batch);
            return;
        }

        var items = new List<ActivityIngestItem>();
        var used = new List<CollectionRecord>();

        foreach (var record in batch)
        {
            if (record.RecordType != CollectionRecordTypes.ActivitySnapshot)
                continue;

            try
            {
                var snap = record.Payload.Deserialize<ActivitySnapshotPayload>(JsonOptions);
                if (snap is null)
                    continue;

                items.Add(new ActivityIngestItem
                {
                    CapturedAt = snap.CapturedAt,
                    KeyboardEventsCount = snap.KeyboardEventsCount,
                    MouseEventsCount = snap.MouseEventsCount,
                    ActiveSeconds = snap.ActiveSeconds,
                    IdleSeconds = snap.IdleSeconds,
                    IntensityScore = snap.IntensityScore,
                    ForegroundProcessName = snap.ForegroundProcessName
                });
                used.Add(record);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Corrupt activity record quarantined eventId={EventId}", record.EventId);
            }
        }

        if (items.Count == 0)
            return;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Activity ingest HTTP failed — re-queue {Count}", used.Count);
            _buffer.RequeueFront(used);
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Accepted
            or System.Net.HttpStatusCode.OK)
        {
            _logger.LogInformation(
                "Activity batch accepted by backend. Count={Count}",
                items.Count);
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Activity ingest rejected status={Status} — records dropped pending re-enrollment",
                (int)response.StatusCode);
            return;
        }

        _logger.LogWarning(
            "Activity ingest non-success status={Status} — re-queue {Count}",
            (int)response.StatusCode,
            used.Count);
        _buffer.RequeueFront(used);
    }
}
