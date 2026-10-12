namespace ONEVO.Agent.Service.Sync;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Flushes buffered collection records to backend ingest endpoints.
/// Uses Device JWT from CredentialStore — never trusts tenant_id in payload.
/// </summary>
public sealed class ActivitySyncService : BackgroundService, IActivityBufferFlush
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
    private readonly PolicyCache? _policyCache;
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    // AGT-01: one tick drains several batches while the backend keeps accepting, but never unbounded.
    internal const int BatchSize = 100;
    internal const int MaxBatchesPerFlush = 10;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FutureTimeRequeueAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StopFlushTimeout = TimeSpan.FromSeconds(10);
    private int _consecutiveTransientFailures;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;

    public ActivitySyncService(
        ILogger<ActivitySyncService> logger,
        ActivityRecordBuffer buffer,
        CredentialStore credentials,
        IHttpClientFactory httpClientFactory,
        IEvidenceProtector protector,
                EvidenceSpoolStore spoolStore,
        IOptions<AgentOptions> options,
        PolicyCache? policyCache = null)

    {
        _logger = logger;
        _buffer = buffer;
        _credentials = credentials;
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _spoolStore = spoolStore;
                _options = options.Value;
        _policyCache = policyCache;

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.IngestIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= _nextAttemptAt)
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

        // Best-effort final flush on stop - bounded so a large backlog cannot hang service shutdown.
        // Anything not sent stays pending in SQLite for the next start.
        try
        {
            using var stopCts = new CancellationTokenSource(StopFlushTimeout);
            await FlushAsync(stopCts.Token);
        }
        catch { /* ignore */ }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        await _flushGate.WaitAsync(ct);
        try
        {
            _buffer.RequeueQuarantined(QuarantineReasons.FutureTime, DateTimeOffset.UtcNow - FutureTimeRequeueAfter);

            for (var i = 0; i < MaxBatchesPerFlush && !ct.IsCancellationRequested; i++)
            {
                var result = await FlushPendingAsync(ct);
                if (result == FlushResult.TransientFailure)
                {
                    RegisterTransientFailure();
                    break;
                }

                _consecutiveTransientFailures = 0;
                _nextAttemptAt = DateTimeOffset.MinValue;
                if (result == FlushResult.Idle)
                    break;
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private enum FlushResult
    {
        /// <summary>Nothing (left) to send, or sending is not possible right now (no JWT / URL).</summary>
        Idle,
        /// <summary>The whole batch was acknowledged or quarantined - another batch may follow.</summary>
        Progress,
        /// <summary>Network/5xx/auth trouble - rows stay pending; back off before the next try.</summary>
        TransientFailure
    }

    private void RegisterTransientFailure()
    {
        _consecutiveTransientFailures++;
        var baseDelay = TimeSpan.FromSeconds(Math.Max(30, _options.IngestIntervalSeconds));
        var factor = Math.Pow(2, Math.Min(_consecutiveTransientFailures - 1, 10));
        var delay = TimeSpan.FromTicks((long)Math.Min(MaxBackoff.Ticks, baseDelay.Ticks * factor));
        delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Max(1, delay.TotalMilliseconds / 5)));
        _nextAttemptAt = DateTimeOffset.UtcNow + delay;
        _logger.LogWarning(
            "Activity sync backing off {Seconds:N0}s after {Failures} consecutive transient failure(s); {Pending} row(s) stay pending",
            delay.TotalSeconds, _consecutiveTransientFailures, _buffer.Count);
    }

    private async Task<FlushResult> FlushPendingAsync(CancellationToken ct)
    {
        var peeked = _buffer.PeekPendingBatch(maxCount: BatchSize);
        if (peeked.Count == 0)
            return FlushResult.Idle;

        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("No device JWT — leaving {Count} records pending", peeked.Count);
            return FlushResult.Idle;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl))
        {
            _logger.LogWarning("ApiBaseUrl not configured — leaving records pending");
            return FlushResult.Idle;
        }

        if (_policyCache is not null)
        {
            var policy = _policyCache.Current;
            var blocked = peeked.Where(item => !IsAllowedByPolicy(item.Record.RecordType, policy)).ToList();
            if (blocked.Count > 0)
            {
                // Not sent and not marked delivered: kept locally with a reason (export/requeue if the
                // capability is turned back on).
                _logger.LogWarning("Quarantining {Count} buffered record(s) disallowed by server policy", blocked.Count);
                foreach (var item in blocked)
                    _buffer.QuarantineRow(item.RowId, QuarantineReasons.PolicyDisabled);
                peeked = peeked.Where(item => IsAllowedByPolicy(item.Record.RecordType, policy)).ToList();
                if (peeked.Count == 0) return FlushResult.Progress;
            }
        }

        // The backend refuses snapshots captured more than 24h ago. After a long offline stretch
        // (e.g. a weekend) the oldest buffered rows are past that window; sending them inside a
        // batch used to get the whole batch 400'd and dropped, taking the fresh rows with it.
        // Quarantine them up front (visible in the buffer, not silently lost) and send only the rest.
        var staleCutoff = DateTimeOffset.UtcNow - MaxSnapshotAge;
        var stale = peeked.Where(item => IsStaleSnapshot(item.Record, staleCutoff)).ToList();
        if (stale.Count > 0)
        {
            _logger.LogWarning(
                "Quarantining {Count} buffered snapshot(s) older than {Hours}h — backend will not accept them",
                stale.Count, MaxSnapshotAge.TotalHours);
            foreach (var item in stale)
                _buffer.QuarantineRow(item.RowId, QuarantineReasons.StaleOver24h);
            peeked = peeked.Where(item => !IsStaleSnapshot(item.Record, staleCutoff)).ToList();
            if (peeked.Count == 0) return FlushResult.Progress;
        }

        var acknowledged = new List<long>();
        var spoolDeletes = new List<string>();
        var index = 0;
        var transient = false;

        while (index < peeked.Count)
        {
            var current = peeked[index];
            var recordType = current.Record.RecordType;

            if (IsBatchableType(recordType))
            {
                var group = CollectAdjacentSameType(peeked, index);
                var records = group.Select(g => g.Record).ToList();
                var outcome = recordType switch
                {
                    CollectionRecordTypes.ActivitySnapshot =>
                        await FlushActivitySnapshotsAsync(records, jwt, ct),
                    CollectionRecordTypes.AppUsageSnapshot =>
                        await FlushAppUsageSnapshotsAsync(records, jwt, ct),
                    CollectionRecordTypes.DeviceStateSnapshot =>
                        await FlushDeviceStateSnapshotsAsync(records, jwt, ct),
                    _ =>
                        await FlushMeetingSignalsAsync(records, jwt, ct)
                };

                ApplyOutcome(group, outcome, acknowledged);
                if (outcome.HasPending)
                {
                    transient = true;
                    break;
                }

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

                transient = true;
                break;
            }

            if (recordType == CollectionRecordTypes.WorkSession)
            {
                var outcome = await FlushWorkSessionAsync(current.Record, jwt, ct);
                ApplyOutcome([current], outcome, acknowledged);
                if (outcome.HasPending)
                {
                    transient = true;
                    break;
                }

                index++;
                continue;
            }

            if (recordType == CollectionRecordTypes.Screenshot)
            {
                var outcome = await FlushScreenshotAsync(current.Record, jwt, ct);
                ApplyOutcome([current], outcome, acknowledged);
                if (outcome.HasPending)
                {
                    transient = true;
                    break;
                }

                // Only a delivered screenshot frees its evidence file; a quarantined one keeps it.
                if (outcome.Acknowledged.Count > 0)
                    spoolDeletes.Add(current.Record.EventId);
                index++;
                continue;
            }

            if (recordType == CollectionRecordTypes.FacePhoto)
            {
                var outcome = await FlushFacePhotoAsync(current, jwt, ct);
                switch (outcome)
                {
                    case FacePhotoFlushOutcome.Acknowledged:
                        acknowledged.Add(current.RowId);
                        index++;
                        continue;
                    case FacePhotoFlushOutcome.Quarantined:
                        index++;
                        continue;
                    default:
                        break;
                }

                transient = true;
                break;
            }

            _logger.LogWarning("Unknown record type {RecordType} — quarantining row {RowId}", recordType, current.RowId);
            _buffer.QuarantineRow(current.RowId, QuarantineReasons.Rejected(0, "unknown_record_type"));
            index++;
        }

        if (acknowledged.Count > 0)
            _buffer.MarkAcknowledged(acknowledged);

        foreach (var eventId in spoolDeletes)
            DeleteSpoolForEvent(eventId);

        return transient ? FlushResult.TransientFailure : FlushResult.Progress;
    }

    private void ApplyOutcome(
        IReadOnlyList<BufferedCollectionRecord> group, SendOutcome outcome, List<long> acknowledged)
    {
        var rowIds = group.ToDictionary(g => g.Record.EventId, g => g.RowId);
        acknowledged.AddRange(outcome.Acknowledged.Select(r => rowIds[r.EventId]));
        foreach (var (record, reason) in outcome.Quarantined)
            _buffer.QuarantineRow(rowIds[record.EventId], reason);
    }

        /// <summary>
        /// Per-record-type capability gate. Internal (not private) so <see cref="AgentWorker"/>'s
        /// HandleCollectionSubmitAsync can reuse this exact mapping to reject a record whose
        /// specific capability is disabled at ingest, instead of only filtering it out here at
        /// flush time — keeping both enforcement points in lockstep by construction.
        /// </summary>
        internal static bool IsAllowedByPolicy(string recordType, AgentPolicy policy) => recordType switch
    {
        CollectionRecordTypes.ActivitySnapshot => policy.ActivitySignalEnabled,
        CollectionRecordTypes.AppUsageSnapshot => policy.AppUsageEnabled,
        CollectionRecordTypes.DeviceStateSnapshot => policy.ActivitySignalEnabled,
        CollectionRecordTypes.MeetingSignal => policy.ActivitySignalEnabled,
        CollectionRecordTypes.Screenshot => policy.ScreenshotEnabled,
        CollectionRecordTypes.InactivityCaptureAttempt => policy.InactivityScreenshotEnabled,
        CollectionRecordTypes.FacePhoto => policy.CameraVerificationEnabled || policy.PhotoRequiredEnabled,
        CollectionRecordTypes.WorkSession => true,
        _ => false
    };

    /// <summary>
    /// Mirrors the backend's per-snapshot freshness window (SnapshotTimeWindow.MaxAge). The backend
    /// stores the in-window items of a mixed batch, so client/server clock skew at the edge only
    /// costs the item itself, never its neighbours.
    /// </summary>
    internal static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromHours(24);

    private static bool IsStaleSnapshot(CollectionRecord record, DateTimeOffset cutoff) =>
        IsBatchableType(record.RecordType)
        && record.CaptureTimestamp != default
        && record.CaptureTimestamp < cutoff;

    private static bool IsBatchableType(string recordType) =>

        recordType is CollectionRecordTypes.ActivitySnapshot
            or CollectionRecordTypes.AppUsageSnapshot
            or CollectionRecordTypes.DeviceStateSnapshot
            or CollectionRecordTypes.MeetingSignal;

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

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // A hardcoded route 404ing is a permanent condition (endpoint missing/moved), not a
                // transient one. Treating it as retryable deadlocks the whole FIFO queue forever: this
                // record never moves, so nothing behind it — including unrelated app-usage/activity
                // snapshots — ever gets a chance to sync (confirmed: 768 app_usage_snapshot rows stuck
                // pending behind exactly this since 2026-08-18).
                _logger.LogWarning(
                    "Inactivity attempt endpoint not found (404) — quarantining eventId={EventId}",
                    buffered.Record.EventId);
                _buffer.QuarantineRow(buffered.RowId, "endpoint_not_found");
                DeleteSpoolForEvent(buffered.Record.EventId);
                return InactivityFlushOutcome.Quarantined;
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
    private async Task<SendOutcome> FlushScreenshotAsync(CollectionRecord record, string jwt, CancellationToken ct)
    {
        ScreenshotPayload? shot;
        byte[] bytes;
        try
        {
            shot = record.Payload.Deserialize<ScreenshotPayload>(JsonOptions)
                ?? throw new JsonException("empty screenshot payload");
            bytes = string.IsNullOrEmpty(shot.Data)
                ? ReadSpooledScreenshot(record.EventId)
                : Convert.FromBase64String(shot.Data);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "Corrupt screenshot record quarantined eventId={EventId}", record.EventId);
            return SendOutcome.QuarantineAll([record], QuarantineReasons.CorruptPayload);
        }

        return await SendAsync(
            AgentApiRoutes.ScreenshotSubmit,
            jwt,
            [new SendEntry(record, null)],
            _ =>
            {
                var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue($"image/{shot.Format}");
                content.Add(fileContent, "file", $"{record.EventId}.{shot.Format}");
                content.Add(new StringContent(shot.CapturedAt.ToString("O")), "capturedAt");
                return content;
            },
            ct);
    }

    private byte[] ReadSpooledScreenshot(string eventId)
    {
        var entry = _buffer.GetEvidenceSpoolEntry(eventId);
        if (entry?.EncryptedPath is null || !Guid.TryParse(eventId, out var captureId))
            throw new InvalidOperationException("missing_screenshot_spool");

        var protectedBytes = File.ReadAllBytes(entry.EncryptedPath);
        return _protector.Unprotect(protectedBytes, captureId);
    }

    private enum FacePhotoFlushOutcome
    {
        Acknowledged,
        Quarantined,
        RetryableFailure
    }

    private async Task<FacePhotoFlushOutcome> FlushFacePhotoAsync(
        BufferedCollectionRecord buffered, string jwt, CancellationToken ct)
    {
        FacePhotoPayload? photo;
        byte[] bytes;
        try
        {
            photo = buffered.Record.Payload.Deserialize<FacePhotoPayload>(JsonOptions);
            if (photo is null)
            {
                _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
                return FacePhotoFlushOutcome.Quarantined;
            }
            bytes = Convert.FromBase64String(photo.Data);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            _logger.LogWarning("Corrupt face photo record quarantined eventId={EventId}", buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "corrupt_payload");
            return FacePhotoFlushOutcome.Quarantined;
        }

        var client = _httpClientFactory.CreateClient("OnevoApi");

        // Step 1: submit check-in to obtain a checkInId
        CheckInSubmitResponse? checkIn;
        using (var req = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.CheckInSubmit)
        {
            Content = JsonContent.Create(new CheckInSubmitRequest
            {
                Latitude         = photo.Latitude,
                Longitude        = photo.Longitude,
                LocationAccuracy = photo.LocationAccuracy,
                LocationAddress  = photo.LocationAddress
            })
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            req.Headers.Add("Idempotency-Key", buffered.Record.EventId);
            HttpResponseMessage resp;
            try { resp = await client.SendAsync(req, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP failed for check-in eventId={EventId}", buffered.Record.EventId);
                return FacePhotoFlushOutcome.RetryableFailure;
            }

            using (resp)
            {
                if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Retryable check-in failure status={Status} eventId={EventId}",
                        (int)resp.StatusCode, buffered.Record.EventId);
                    return FacePhotoFlushOutcome.RetryableFailure;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Check-in rejected status={Status} eventId={EventId}",
                        (int)resp.StatusCode, buffered.Record.EventId);
                    _buffer.QuarantineRow(buffered.RowId, "check_in_rejected");
                    return FacePhotoFlushOutcome.Quarantined;
                }

                checkIn = await resp.Content.ReadFromJsonAsync<CheckInSubmitResponse>(JsonOptions, ct);
                if (checkIn is null)
                {
                    _logger.LogWarning("Null check-in response eventId={EventId}", buffered.Record.EventId);
                    _buffer.QuarantineRow(buffered.RowId, "null_check_in_response");
                    return FacePhotoFlushOutcome.Quarantined;
                }
            }
        }

        // Step 2: upload the face scan photo (check-in already exists — don't retry on failure to avoid duplicates)
        var faceScanRoute = string.Format(AgentApiRoutes.FaceScanUpload, checkIn.CheckInId);
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue($"image/{photo.Format}");
        multipart.Add(fileContent, "face_scan", $"{buffered.Record.EventId}.{photo.Format}");

        using var faceScanReq = new HttpRequestMessage(HttpMethod.Post, faceScanRoute) { Content = multipart };
        faceScanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage faceScanResp;
        try { faceScanResp = await client.SendAsync(faceScanReq, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP failed for face-scan upload checkInId={CheckInId}", checkIn.CheckInId);
            _buffer.QuarantineRow(buffered.RowId, "face_scan_upload_failed");
            return FacePhotoFlushOutcome.Quarantined;
        }

        using (faceScanResp)
        {
            if (faceScanResp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Face photo accepted checkInId={CheckInId} eventId={EventId}",
                    checkIn.CheckInId, buffered.Record.EventId);
                return FacePhotoFlushOutcome.Acknowledged;
            }

            _logger.LogWarning("Face-scan upload failed status={Status} checkInId={CheckInId} eventId={EventId}",
                (int)faceScanResp.StatusCode, checkIn.CheckInId, buffered.Record.EventId);
            _buffer.QuarantineRow(buffered.RowId, "face_scan_rejected");
            return FacePhotoFlushOutcome.Quarantined;
        }
    }

    private async Task<SendOutcome> FlushActivitySnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        var (entries, items, corrupt) = ParseBatch<ActivitySnapshotPayload, ActivityIngestItem>(
            records,
            snap => (snap.CapturedAt, new ActivityIngestItem
            {
                CapturedAt           = snap.CapturedAt,
                KeyboardEventsCount  = snap.KeyboardEventsCount,
                MouseEventsCount     = snap.MouseEventsCount,
                ActiveSeconds        = snap.ActiveSeconds,
                IdleSeconds          = snap.IdleSeconds,
                IntensityScore       = snap.IntensityScore,
                ForegroundProcessName = snap.ForegroundProcessName
            }));

        var outcome = await SendJsonBatchAsync(
            AgentApiRoutes.ActivitySnapshots, jwt, entries, items,
            selected => new ActivityIngestRequest { Snapshots = selected }, ct);
        return outcome.WithQuarantined(corrupt, QuarantineReasons.CorruptPayload);
    }

    private async Task<SendOutcome> FlushAppUsageSnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        var (entries, items, corrupt) = ParseBatch<AppUsageSnapshotPayload, AppUsageIngestItem>(
            records,
            snap => (snap.CapturedAt, new AppUsageIngestItem
            {
                CapturedAt      = snap.CapturedAt,
                ProcessName     = snap.ProcessName,
                WindowTitleHash = snap.WindowTitleHash
            }));

        var outcome = await SendJsonBatchAsync(
            AgentApiRoutes.AppUsageSnapshots, jwt, entries, items,
            selected => new AppUsageIngestRequest { Snapshots = selected }, ct);
        return outcome.WithQuarantined(corrupt, QuarantineReasons.CorruptPayload);
    }

    private async Task<SendOutcome> FlushDeviceStateSnapshotsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        var (entries, items, corrupt) = ParseBatch<DeviceStateSnapshotPayload, DeviceStateIngestItem>(
            records,
            snap => (snap.CapturedAt, new DeviceStateIngestItem
            {
                CapturedAt     = snap.CapturedAt,
                IdleSeconds    = snap.IdleSeconds,
                IsIdle         = snap.IsIdle,
                Latitude       = snap.Latitude,
                Longitude      = snap.Longitude,
                AccuracyMeters = snap.AccuracyMeters
            }));

        var outcome = await SendJsonBatchAsync(
            AgentApiRoutes.DeviceStateSnapshots, jwt, entries, items,
            selected => new DeviceStateIngestRequest { Snapshots = selected }, ct);
        return outcome.WithQuarantined(corrupt, QuarantineReasons.CorruptPayload);
    }

    private async Task<SendOutcome> FlushMeetingSignalsAsync(
        List<CollectionRecord> records, string jwt, CancellationToken ct)
    {
        var (entries, items, corrupt) = ParseBatch<MeetingSignalPayload, MeetingSignalIngestItem>(
            records,
            signal => (signal.CapturedAt, new MeetingSignalIngestItem
            {
                CapturedAt          = signal.CapturedAt,
                IsMeetingAppRunning = signal.IsMeetingAppRunning,
                ProcessName         = signal.ProcessName
            }));

        var outcome = await SendJsonBatchAsync(
            AgentApiRoutes.MeetingSignals, jwt, entries, items,
            selected => new MeetingSignalIngestRequest { Signals = selected }, ct);
        return outcome.WithQuarantined(corrupt, QuarantineReasons.CorruptPayload);
    }

    /// <summary>
    /// One POST per completed session (no batching — sessions are rare compared to
    /// activity snapshots). Each is upserted server-side by SessionId, so a retried
    /// delivery after a dropped response is a no-op, never a duplicate row.
    /// </summary>
    private async Task<SendOutcome> FlushWorkSessionAsync(CollectionRecord record, string jwt, CancellationToken ct)
    {
        WorkSessionPayload? session;
        try
        {
            session = record.Payload.Deserialize<WorkSessionPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            session = null;
        }

        if (session is null)
        {
            _logger.LogWarning("Corrupt work-session record quarantined eventId={EventId}", record.EventId);
            return SendOutcome.QuarantineAll([record], QuarantineReasons.CorruptPayload);
        }

        var body = new WorkSessionSubmitRequest
        {
            SessionId = session.SessionId,
            ClockInAt = session.ClockInAt,
            ClockOutAt = session.ClockOutAt,
            AccumulatedBreakSeconds = (int)session.AccumulatedBreak.TotalSeconds,
            AccumulatedWorkSeconds = (int)session.AccumulatedWork.TotalSeconds,
            AccumulatedIdleSeconds = (int)session.AccumulatedIdle.TotalSeconds,
            BreakSessionCount = session.BreakSessionCount,
            ScheduleDisplay = session.ScheduleDisplay
        };

        return await SendAsync(
            AgentApiRoutes.WorkSessionSubmit, jwt, [new SendEntry(record, null)], _ => JsonContent.Create(body), ct);
    }

    private (List<SendEntry> Entries, List<TItem> Items, List<CollectionRecord> Corrupt) ParseBatch<TPayload, TItem>(
        List<CollectionRecord> records, Func<TPayload, (DateTimeOffset CapturedAt, TItem Item)> map)
        where TPayload : class
    {
        var entries = new List<SendEntry>();
        var items = new List<TItem>();
        var corrupt = new List<CollectionRecord>();

        foreach (var record in records)
        {
            TPayload? payload;
            try
            {
                payload = record.Payload.Deserialize<TPayload>(JsonOptions);
            }
            catch (JsonException)
            {
                payload = null;
            }

            if (payload is null)
            {
                _logger.LogWarning(
                    "Corrupt {RecordType} record quarantined eventId={EventId}", record.RecordType, record.EventId);
                corrupt.Add(record);
                continue;
            }

            var (capturedAt, item) = map(payload);
            entries.Add(new SendEntry(record, capturedAt));
            items.Add(item);
        }

        return (entries, items, corrupt);
    }

    private Task<SendOutcome> SendJsonBatchAsync<TItem>(
        string route,
        string jwt,
        List<SendEntry> entries,
        List<TItem> items,
        Func<List<TItem>, object> body,
        CancellationToken ct)
    {
        if (entries.Count == 0)
            return Task.FromResult(new SendOutcome());

        var itemOf = entries.Select((entry, i) => (entry.Record.EventId, Item: items[i]))
            .ToDictionary(x => x.EventId, x => x.Item);
        return SendAsync(
            route, jwt, entries,
            selected => JsonContent.Create(body(selected.Select(e => itemOf[e.Record.EventId]).ToList())),
            ct);
    }

    /// <summary>
    /// Sends <paramref name="entries"/> in one request and decides, per record, whether it is
    /// acknowledged (server stored it, or already had it), quarantined (server will never accept it —
    /// kept locally with the reason) or left pending (transient: network, 5xx, auth). A validation
    /// rejection of a multi-record request is retried record by record so one bad record never takes
    /// valid neighbours down with it.
    /// </summary>
    private async Task<SendOutcome> SendAsync(
        string route,
        string jwt,
        IReadOnlyList<SendEntry> entries,
        Func<IReadOnlyList<SendEntry>, HttpContent> content,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = content(entries) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Includes "request reached the server but the response was lost": the rows stay pending
            // and the replay is deduplicated server-side.
            _logger.LogWarning(ex, "HTTP failed for {Route} — {Count} record(s) stay pending", route, entries.Count);
            return SendOutcome.PendingAll(entries);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return await ReadAcceptedAsync(route, response, entries, ct);

            var (code, detail) = await ReadProblemAsync(response, ct);

            if (response.StatusCode == HttpStatusCode.Forbidden && IsCapabilityDisabled(code, detail))
            {
                _logger.LogWarning(
                    "{Route} capability disabled for this employee — quarantining {Count} record(s)", route, entries.Count);
                return SendOutcome.QuarantineAll(entries.Select(e => e.Record), QuarantineReasons.PolicyDisabled);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                || status >= 500)
            {
                _logger.LogWarning(
                    "Status {Status} for {Route} — {Count} record(s) stay pending for retry", status, route, entries.Count);
                return SendOutcome.PendingAll(entries);
            }

            // Remaining 4xx: the server refused the content itself.
            if (code is SnapshotTooOldCode or SnapshotFutureTimeCode)
            {
                // The server judged every item in this request out of its time window.
                var reason = code == SnapshotFutureTimeCode
                    ? QuarantineReasons.FutureTime
                    : QuarantineReasons.Rejected(status, code);
                _logger.LogWarning("{Route} rejected {Count} record(s) ({Code}) — quarantined", route, entries.Count, code);
                return SendOutcome.QuarantineAll(entries.Select(e => e.Record), reason);
            }

            if (entries.Count > 1)
            {
                _logger.LogWarning(
                    "Status {Status} for {Route} batch of {Count} — retrying record by record", status, route, entries.Count);
                var combined = new SendOutcome();
                foreach (var entry in entries)
                {
                    var single = await SendAsync(route, jwt, [entry], content, ct);
                    combined.Add(single);
                    if (single.HasPending)
                    {
                        combined.Pending.AddRange(entries.SkipWhile(e => e != entry).Skip(1).Select(e => e.Record));
                        break;
                    }
                }

                return combined;
            }

            _logger.LogWarning(
                "Status {Status} ({Code}) for {Route} — record {EventId} quarantined, will not be retried as-is",
                status, code ?? "none", route, entries[0].Record.EventId);
            return SendOutcome.QuarantineAll([entries[0].Record], QuarantineReasons.Rejected(status, code));
        }
    }

    private const string SnapshotTooOldCode = "monitoring.snapshot_too_old";
    private const string SnapshotFutureTimeCode = "monitoring.snapshot_future_time";

    private async Task<SendOutcome> ReadAcceptedAsync(
        string route, HttpResponseMessage response, IReadOnlyList<SendEntry> entries, CancellationToken ct)
    {
        var rejectedCodes = new Dictionary<long, string>();
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var parsed = JsonSerializer.Deserialize<SnapshotIngestResponse>(body, JsonOptions);
                foreach (var rejection in parsed?.Rejected ?? [])
                    rejectedCodes[MicrosecondKey(rejection.CapturedAt)] = rejection.Code;
            }
        }
        catch (JsonException)
        {
            // Older backends answer 202 with no (or a different) body: everything sent counts as accepted.
        }

        var outcome = new SendOutcome();
        foreach (var entry in entries)
        {
            if (entry.CapturedAt is { } at && rejectedCodes.TryGetValue(MicrosecondKey(at), out var code))
            {
                outcome.Quarantined.Add((entry.Record, code == SnapshotFutureTimeCode
                    ? QuarantineReasons.FutureTime
                    : QuarantineReasons.Rejected((int)response.StatusCode, code)));
            }
            else
            {
                outcome.Acknowledged.Add(entry.Record);
            }
        }

        if (outcome.Quarantined.Count > 0)
            _logger.LogWarning(
                "{Route} stored {Accepted} record(s) and refused {Rejected} — refused ones quarantined",
                route, outcome.Acknowledged.Count, outcome.Quarantined.Count);
        else
            _logger.LogInformation("Batch accepted for {Route}. Count={Count}", route, entries.Count);
        return outcome;
    }

    private static async Task<(string? Code, string? Detail)> ReadProblemAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return (null, null);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var detail = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

            // Backends that predate the "code" extension only send the human message.
            code ??= detail switch
            {
                not null when detail.Contains("older than 24 hours", StringComparison.OrdinalIgnoreCase) => SnapshotTooOldCode,
                not null when detail.Contains("cannot be in the future", StringComparison.OrdinalIgnoreCase) => SnapshotFutureTimeCode,
                _ => null
            };
            return (code, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static bool IsCapabilityDisabled(string? code, string? detail) =>
        (code?.EndsWith("_disabled", StringComparison.Ordinal) ?? false)
        || (detail?.Contains("is not enabled for this employee", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>The backend stores and reports captured_at at microsecond precision.</summary>
    private static long MicrosecondKey(DateTimeOffset at) => at.UtcTicks / TimeSpan.TicksPerMicrosecond;

    private sealed record SendEntry(CollectionRecord Record, DateTimeOffset? CapturedAt);

    private sealed class SendOutcome
    {
        public List<CollectionRecord> Acknowledged { get; } = [];
        public List<(CollectionRecord Record, string Reason)> Quarantined { get; } = [];
        public List<CollectionRecord> Pending { get; } = [];
        public bool HasPending => Pending.Count > 0;

        public static SendOutcome PendingAll(IEnumerable<SendEntry> entries)
        {
            var outcome = new SendOutcome();
            outcome.Pending.AddRange(entries.Select(e => e.Record));
            return outcome;
        }

        public static SendOutcome QuarantineAll(IEnumerable<CollectionRecord> records, string reason) =>
            new SendOutcome().WithQuarantined(records, reason);

        public SendOutcome WithQuarantined(IEnumerable<CollectionRecord> records, string reason)
        {
            Quarantined.AddRange(records.Select(r => (r, reason)));
            return this;
        }

        public void Add(SendOutcome other)
        {
            Acknowledged.AddRange(other.Acknowledged);
            Quarantined.AddRange(other.Quarantined);
            Pending.AddRange(other.Pending);
        }
    }
}
