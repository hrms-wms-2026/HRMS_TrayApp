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
    private readonly IEvidenceProtector _protector;
    private readonly EvidenceSpoolStore _spoolStore;
    private readonly AgentOptions _options;

    public ActivitySyncService(
        ILogger<ActivitySyncService> logger,
        ActivityRecordBuffer buffer,
        CredentialStore credentials,
        IHttpClientFactory httpClientFactory,
        IEvidenceProtector protector,
        EvidenceSpoolStore spoolStore,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _buffer = buffer;
        _credentials = credentials;
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _spoolStore = spoolStore;
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
        var peeked = _buffer.PeekPendingBatch(maxCount: 100);
        if (peeked.Count == 0)
            return;

        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("No device JWT — leaving {Count} records pending", peeked.Count);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl not configured — leaving records pending");
            return;
        }

        var acknowledged = new List<long>();
        var spoolDeletes = new List<string>();
        var index = 0;

        while (index < peeked.Count)
        {
            var current = peeked[index];
            var recordType = current.Record.RecordType;

            if (IsBatchableType(recordType))
            {
                var group = CollectAdjacentSameType(peeked, index);
                var records = group.Select(g => g.Record).ToList();
                var failed = recordType switch
                {
                    CollectionRecordTypes.ActivitySnapshot =>
                        await FlushActivitySnapshotsAsync(records, jwt, ct),
                    CollectionRecordTypes.AppUsageSnapshot =>
                        await FlushAppUsageSnapshotsAsync(records, jwt, ct),
                    CollectionRecordTypes.DeviceStateSnapshot =>
                        await FlushDeviceStateSnapshotsAsync(records, jwt, ct),
                    _ => records
                };

                if (failed.Count > 0)
                {
                    var failedIds = failed.Select(r => r.EventId).ToHashSet();
                    acknowledged.AddRange(
                        group.Where(g => !failedIds.Contains(g.Record.EventId)).Select(g => g.RowId));
                    break;
                }

                acknowledged.AddRange(group.Select(g => g.RowId));
                index += group.Count;
                continue;
            }

            if (recordType == CollectionRecordTypes.InactivityCaptureAttempt)
            {
                var result = await FlushInactivityAttemptAsync(current, jwt, ct);
                switch (result)
                {
                    case InactivityFlushOutcome.Acknowledged:
                        acknowledged.Add(current.RowId);
                        spoolDeletes.Add(current.Record.EventId);
                        index++;
                        continue;
                    case InactivityFlushOutcome.Quarantined:
                        index++;
                        continue;
                    default:
                        break;
                }

                break;
            }

            if (recordType == CollectionRecordTypes.WorkSession)
            {
                var failed = await FlushWorkSessionsAsync([current.Record], jwt, ct);
                if (failed.Count > 0)
                    break;

                acknowledged.Add(current.RowId);
                index++;
                continue;
            }

            if (recordType == CollectionRecordTypes.Screenshot)
            {
                var failed = await FlushScreenshotsAsync([current.Record], jwt, ct);
                if (failed.Count > 0)
                    break;

                acknowledged.Add(current.RowId);
                index++;
                continue;
            }

            _logger.LogWarning("Unknown record type {RecordType} — skipping row {RowId}", recordType, current.RowId);
            index++;
        }

        if (acknowledged.Count > 0)
            _buffer.MarkAcknowledged(acknowledged);

        foreach (var eventId in spoolDeletes)
            DeleteSpoolForEvent(eventId);
    }

    private static bool IsBatchableType(string recordType) =>
        recordType is CollectionRecordTypes.ActivitySnapshot
            or CollectionRecordTypes.AppUsageSnapshot
            or CollectionRecordTypes.DeviceStateSnapshot;

    private static List<BufferedCollectionRecord> CollectAdjacentSameType(
        IReadOnlyList<BufferedCollectionRecord> peeked, int startIndex)
    {
        var type = peeked[startIndex].Record.RecordType;
        var group = new List<BufferedCollectionRecord> { peeked[startIndex] };
        var i = startIndex + 1;
        while (i < peeked.Count && peeked[i].Record.RecordType == type)
        {
            group.Add(peeked[i]);
            i++;
        }

        return group;
    }

    private enum InactivityFlushOutcome
    {
        Acknowledged,
        Quarantined,
        RetryableFailure
    }

    private async Task<InactivityFlushOutcome> FlushInactivityAttemptAsync(
        BufferedCollectionRecord buffered, string jwt, CancellationToken ct)
    {
        InactivityCaptureAttemptPayload attempt;
        try
        {
            var parsed = buffered.Record.Payload.Deserialize<InactivityCaptureAttemptPayload>(JsonOptions);
            if (parsed is null)
            {
                _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
                DeleteSpoolForEvent(buffered.Record.EventId);
                return InactivityFlushOutcome.Quarantined;
            }

            attempt = parsed;
        }
        catch (JsonException)
        {
            _logger.LogWarning("Corrupt inactivity record quarantined eventId={EventId}", buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
            DeleteSpoolForEvent(buffered.Record.EventId);
            return InactivityFlushOutcome.Quarantined;
        }

        using var content = BuildInactivityMultipart(attempt, buffered.Record.EventId);

        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.InactivityAttemptSubmit)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP failed for inactivity attempt eventId={EventId}", buffered.Record.EventId);
            return InactivityFlushOutcome.RetryableFailure;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted)
            {
                _logger.LogInformation("Inactivity attempt accepted eventId={EventId}", buffered.Record.EventId);
                return InactivityFlushOutcome.Acknowledged;
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                if (await IsAttemptAlreadyRecordedAsync(response, ct))
                {
                    _logger.LogInformation(
                        "Inactivity attempt already recorded eventId={EventId}", buffered.Record.EventId);
                    return InactivityFlushOutcome.Acknowledged;
                }

                _logger.LogWarning("Conflicting inactivity retry quarantined eventId={EventId}", buffered.Record.EventId);
                _buffer.QuarantineRow(buffered.RowId, "conflicting_retry");
                DeleteSpoolForEvent(buffered.Record.EventId);
                return InactivityFlushOutcome.Quarantined;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning("Inactivity validation failed eventId={EventId}", buffered.Record.EventId);
                _buffer.QuarantineRow(buffered.RowId, "validation_failed");
                DeleteSpoolForEvent(buffered.Record.EventId);
                return InactivityFlushOutcome.Quarantined;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Inactivity upload unauthorized — leaving pending eventId={EventId}",
                    buffered.Record.EventId);
                return InactivityFlushOutcome.RetryableFailure;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Inactivity upload policy rejected eventId={EventId}", buffered.Record.EventId);
                _buffer.QuarantineRow(buffered.RowId, "policy_rejected");
                DeleteSpoolForEvent(buffered.Record.EventId);
                return InactivityFlushOutcome.Quarantined;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500)
            {
                _logger.LogWarning(
                    "Retryable inactivity upload failure status={Status} eventId={EventId}",
                    (int)response.StatusCode, buffered.Record.EventId);
                return InactivityFlushOutcome.RetryableFailure;
            }

            _logger.LogWarning(
                "Unexpected inactivity upload status={Status} eventId={EventId}",
                (int)response.StatusCode, buffered.Record.EventId);
            return InactivityFlushOutcome.RetryableFailure;
        }
    }

    private MultipartFormDataContent BuildInactivityMultipart(
        InactivityCaptureAttemptPayload attempt, string eventId)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(attempt.AttemptId.ToString("N")), InactivityAttemptFormFields.AttemptId);
        content.Add(new StringContent(attempt.PolicyVersion), InactivityAttemptFormFields.PolicyVersion);
        content.Add(new StringContent(attempt.IdleStartedAt.ToString("O")), InactivityAttemptFormFields.IdleStartedAt);
        content.Add(new StringContent(attempt.PromptedAt.ToString("O")), InactivityAttemptFormFields.PromptedAt);

        if (attempt.DecisionAt is { } decisionAt)
            content.Add(new StringContent(decisionAt.ToString("O")), InactivityAttemptFormFields.DecisionAt);
        if (attempt.CapturedAt is { } capturedAt)
            content.Add(new StringContent(capturedAt.ToString("O")), InactivityAttemptFormFields.CapturedAt);

        content.Add(new StringContent(attempt.IdleDurationSeconds.ToString()), InactivityAttemptFormFields.IdleDurationSeconds);
        content.Add(new StringContent(attempt.MonitorCount.ToString()), InactivityAttemptFormFields.MonitorCount);
        content.Add(new StringContent(attempt.Outcome), InactivityAttemptFormFields.Outcome);

        if (!string.IsNullOrWhiteSpace(attempt.FailureCode))
            content.Add(new StringContent(attempt.FailureCode), InactivityAttemptFormFields.FailureCode);
        if (!string.IsNullOrWhiteSpace(attempt.ContentType))
            content.Add(new StringContent(attempt.ContentType), InactivityAttemptFormFields.ContentType);
        if (!string.IsNullOrWhiteSpace(attempt.Sha256))
            content.Add(new StringContent(attempt.Sha256), InactivityAttemptFormFields.Sha256);

        if (attempt.Outcome == InactivityCaptureOutcomes.Captured)
        {
            var spoolEntry = _buffer.GetEvidenceSpoolEntry(eventId);
            if (spoolEntry?.EncryptedPath is not null)
            {
                var protectedBytes = _spoolStore.Read(spoolEntry.EncryptedPath, attempt.AttemptId);
                var jpegBytes = _protector.Unprotect(protectedBytes, attempt.AttemptId);
                var fileContent = new ByteArrayContent(jpegBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(attempt.ContentType ?? "image/jpeg");
                content.Add(fileContent, InactivityAttemptFormFields.File, $"{attempt.AttemptId:N}.jpg");
            }
        }

        return content;
    }

    private static async Task<bool> IsAttemptAlreadyRecordedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Contains("attempt_already_recorded", StringComparison.OrdinalIgnoreCase))
                return true;

            using var doc = JsonDocument.Parse(body);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                if (property.Value.GetString()?.Contains("attempt_already_recorded", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }
        catch
        {
            // Non-JSON 409 bodies are treated as conflicting retries.
        }

        return false;
    }

    private void DeleteSpoolForEvent(string eventId)
    {
        var entry = _buffer.GetEvidenceSpoolEntry(eventId);
        if (entry?.EncryptedPath is not null)
            _spoolStore.Delete(entry.EncryptedPath);
        _buffer.DeleteEvidenceSpoolEntry(eventId);
    }

    /// <summary>
    /// One POST per screenshot (multipart — each carries a full image, no JSON batching).
    /// </summary>
    private async Task<List<CollectionRecord>> FlushScreenshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        if (records.Count == 0) return [];

        var requeue = new List<CollectionRecord>();
        foreach (var record in records)
        {
            ScreenshotPayload? shot;
            byte[] bytes;
            try
            {
                shot = record.Payload.Deserialize<ScreenshotPayload>(JsonOptions);
                if (shot is null) continue;
                bytes = Convert.FromBase64String(shot.Data);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                _logger.LogWarning("Corrupt screenshot record quarantined eventId={EventId}", record.EventId);
                continue;
            }

            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue($"image/{shot.Format}");
            content.Add(fileContent, "file", $"{record.EventId}.{shot.Format}");
            content.Add(new StringContent(shot.CapturedAt.ToString("O")), "capturedAt");

            var failed = await PostFormAsync(AgentApiRoutes.ScreenshotSubmit, jwt, content, [record], ct);
            requeue.AddRange(failed);
        }

        return requeue;
    }

    private async Task<List<CollectionRecord>> PostFormAsync(
        string route, string jwt, HttpContent content, List<CollectionRecord> records, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = content };
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
