namespace ONEVO.Agent.Service.Buffer;

using Microsoft.Extensions.Logging;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Reassembles a working-hours screenshot sent in IPC chunks and queues it for immediate upload.
/// </summary>
public sealed class PeriodicScreenshotHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Transfer> _transfers = new();
    private readonly ActivityRecordBuffer _buffer;
    private readonly EvidenceSpoolStore _spool;
    private readonly IEvidenceProtector _protector;
    private readonly AgentStateMachine _stateMachine;
    private readonly PolicyCache _policyCache;
    private readonly DeviceIdentityStore _deviceIdentity;
    private readonly ILogger<PeriodicScreenshotHandler> _logger;

    public PeriodicScreenshotHandler(
        ActivityRecordBuffer buffer,
        EvidenceSpoolStore spool,
        IEvidenceProtector protector,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        DeviceIdentityStore deviceIdentity,
        ILogger<PeriodicScreenshotHandler> logger)
    {
        _buffer = buffer;
        _spool = spool;
        _protector = protector;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _deviceIdentity = deviceIdentity;
        _logger = logger;
    }

    public EvidenceTransferAckPayload HandleStart(PeriodicScreenshotStartPayload start, DateTimeOffset now)
    {
        PurgeExpired(now);
        if (_stateMachine.CurrentState != MonitoringState.Active)
            return Reject(start.CaptureId, "monitoring_not_active");
        if (!CanCapture())
            return Reject(start.CaptureId, "screenshot_disabled");
        if (start.TotalBytes <= 0 || start.TotalBytes > Constants.MaxScreenshotBytes || start.ChunkCount <= 0)
            return Reject(start.CaptureId, "invalid_start");

        var expected = (int)Math.Ceiling(start.TotalBytes / (double)Constants.EvidenceChunkSizeBytes);
        if (start.ChunkCount != expected)
            return Reject(start.CaptureId, "chunk_count_mismatch");

        lock (_gate)
        {
            if (_transfers.ContainsKey(start.CaptureId))
                return Reject(start.CaptureId, "duplicate_transfer");
            if (_transfers.Count >= 2)
                return Reject(start.CaptureId, "too_many_transfers");
            _transfers[start.CaptureId] = new Transfer(start.CapturedAt, start.TotalBytes, start.ChunkCount, now);
        }

        return new EvidenceTransferAckPayload(start.CaptureId, true, null);
    }

    public EvidenceTransferAckPayload HandleChunk(PeriodicScreenshotChunkPayload chunk, DateTimeOffset now)
    {
        PurgeExpired(now);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(chunk.DataBase64);
        }
        catch (FormatException)
        {
            return Reject(chunk.CaptureId, "invalid_chunk");
        }

        lock (_gate)
        {
            if (!_transfers.TryGetValue(chunk.CaptureId, out var transfer))
                return Reject(chunk.CaptureId, "unknown_transfer");
            if (chunk.Index < 0 || chunk.Index >= transfer.ChunkCount || transfer.Chunks[chunk.Index] is not null)
                return Reject(chunk.CaptureId, "invalid_chunk");
            transfer.Chunks[chunk.Index] = bytes;
            transfer.ReceivedBytes += bytes.Length;
        }

        return new EvidenceTransferAckPayload(chunk.CaptureId, true, null);
    }

    public EvidenceTransferAckPayload HandleComplete(Guid captureId, DateTimeOffset now)
    {
        Transfer? transfer;
        lock (_gate)
        {
            if (!_transfers.Remove(captureId, out transfer))
                return Reject(captureId, "unknown_transfer");
        }

        if (!CanCapture() || _stateMachine.CurrentState != MonitoringState.Active)
            return Reject(captureId, "screenshot_disabled");
        if (transfer.Chunks.Any(chunk => chunk is null) || transfer.ReceivedBytes != transfer.TotalBytes)
            return Reject(captureId, "incomplete_transfer");

        var jpeg = new byte[transfer.TotalBytes];
        var offset = 0;
        foreach (var chunk in transfer.Chunks)
        {
            chunk!.CopyTo(jpeg, offset);
            offset += chunk.Length;
        }

        string? spoolPath = null;
        try
        {
            var protectedBytes = _protector.Protect(jpeg, captureId);
            if (!_spool.HasCapacityFor(protectedBytes.Length))
                return Reject(captureId, "evidence_spool_quota_exceeded");
            spoolPath = _spool.Write(captureId, protectedBytes);
            var deviceId = _deviceIdentity.Load()?.DeviceId ?? "unknown";
            var queued = _buffer.TryEnqueuePeriodicScreenshot(
                captureId,
                transfer.CapturedAt,
                deviceId,
                spoolPath,
                protectedBytes.Length,
                now.Add(EvidenceSpoolStore.Retention));
            if (!queued)
            {
                _spool.Delete(spoolPath);
                return Reject(captureId, "queue_full");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue periodic screenshot {CaptureId}", captureId);
            if (spoolPath is not null)
                _spool.Delete(spoolPath);
            return Reject(captureId, "spool_write_failed");
        }

        _logger.LogInformation("Periodic screenshot queued CaptureId={CaptureId} Bytes={Bytes}", captureId, jpeg.Length);
        return new EvidenceTransferAckPayload(captureId, true, null);
    }

    private bool CanCapture()
    {
        var policy = _policyCache.Current;
        return policy.ScreenshotEnabled && policy.ActivitySignalEnabled;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _transfers
                .Where(pair => now - pair.Value.StartedAt > TimeSpan.FromMinutes(2))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var id in expired)
                _transfers.Remove(id);
        }
    }

    private static EvidenceTransferAckPayload Reject(Guid captureId, string code) =>
        new(captureId, false, code);

    private sealed class Transfer(DateTimeOffset capturedAt, int totalBytes, int chunkCount, DateTimeOffset startedAt)
    {
        public DateTimeOffset CapturedAt { get; } = capturedAt;
        public int TotalBytes { get; } = totalBytes;
        public int ChunkCount { get; } = chunkCount;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public byte[]?[] Chunks { get; } = new byte[chunkCount][];
        public int ReceivedBytes { get; set; }
    }
}
