namespace ONEVO.Agent.Service.Buffer;

using System.Text.Json;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Validates policy/state, persists completed evidence transfers to SQLite + encrypted spool,
/// and returns privacy-safe acknowledgement payloads for the Tray App.
/// </summary>
public sealed class InactivityEvidenceHandler
{
    private readonly EvidenceTransferAssembler _assembler;
    private readonly ActivityRecordBuffer _buffer;
    private readonly EvidenceSpoolStore _spool;
    private readonly IEvidenceProtector _protector;
    private readonly AgentStateMachine _stateMachine;
    private readonly PolicyCache _policyCache;
    private readonly DeviceIdentityStore _deviceIdentity;
    private readonly ILogger<InactivityEvidenceHandler> _logger;

    public InactivityEvidenceHandler(
        EvidenceTransferAssembler assembler,
        ActivityRecordBuffer buffer,
        EvidenceSpoolStore spool,
        IEvidenceProtector protector,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        DeviceIdentityStore deviceIdentity,
        ILogger<InactivityEvidenceHandler> logger)
    {
        _assembler = assembler;
        _buffer = buffer;
        _spool = spool;
        _protector = protector;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _deviceIdentity = deviceIdentity;
        _logger = logger;
    }

    public EvidenceTransferAckPayload HandleStart(
        EvidenceTransferStartPayload start,
        DateTimeOffset now)
    {
        var policyError = ValidatePolicy(start.Attempt);
        if (policyError is not null)
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, false, policyError);

        if (_stateMachine.CurrentState != MonitoringState.Active)
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, false, "monitoring_not_active");

        if (start.Attempt.IdleDurationSeconds < Constants.InactivityThresholdSeconds)
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, false, "idle_too_short");

        if (_buffer.HasPendingOrSyncedAttempt(start.Attempt.AttemptId))
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, true, null);

        var result = _assembler.HandleStart(start, now);
        return new EvidenceTransferAckPayload(result.AttemptId, result.IsAccepted, result.ErrorCode);
    }

    public EvidenceTransferAckPayload HandleChunk(
        EvidenceTransferChunkPayload chunk,
        DateTimeOffset now)
    {
        var result = _assembler.HandleChunk(chunk, now);
        return new EvidenceTransferAckPayload(result.AttemptId, result.IsAccepted, result.ErrorCode);
    }

    public EvidenceTransferAckPayload HandleComplete(Guid attemptId, DateTimeOffset now)
    {
        var result = _assembler.TryComplete(attemptId, now);
        if (!result.Accepted || result.Attempt is null)
            return new EvidenceTransferAckPayload(attemptId, false, result.ErrorCode);

        var attempt = result.Attempt;
        var policyError = ValidatePolicy(attempt);
        if (policyError is not null)
            return new EvidenceTransferAckPayload(attemptId, false, policyError);

        if (attempt.Outcome == InactivityCaptureOutcomes.Captured)
        {
            if (result.JpegBytes.IsEmpty)
                return new EvidenceTransferAckPayload(attemptId, false, "missing_image");

            if (attempt.MonitorCount < 1)
                return new EvidenceTransferAckPayload(attemptId, false, "invalid_monitor_count");
        }
        else if (!result.JpegBytes.IsEmpty)
        {
            return new EvidenceTransferAckPayload(attemptId, false, "unexpected_image");
        }

        if (_buffer.HasPendingOrSyncedAttempt(attemptId))
            return new EvidenceTransferAckPayload(attemptId, true, null);

        string? spoolPath = null;
        var encryptedSize = 0;
        if (!result.JpegBytes.IsEmpty)
        {
            try
            {
                var protectedBytes = _protector.Protect(result.JpegBytes, attemptId);
                if (!_spool.HasCapacityFor(protectedBytes.Length))
                    return new EvidenceTransferAckPayload(attemptId, false, "evidence_spool_quota_exceeded");

                spoolPath = _spool.Write(attemptId, protectedBytes);
                encryptedSize = protectedBytes.Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to spool evidence AttemptId={AttemptId}", attemptId);
                return new EvidenceTransferAckPayload(attemptId, false, "spool_write_failed");
            }
        }

        var deviceId = _deviceIdentity.Load()?.DeviceId ?? "unknown";
        var expiresAt = now.Add(EvidenceSpoolStore.Retention);
        var persisted = _buffer.TryEnqueueInactivityAttempt(
            attempt,
            deviceId,
            spoolPath,
            encryptedSize,
            expiresAt);

        if (!persisted)
        {
            _spool.Delete(spoolPath);
            return new EvidenceTransferAckPayload(attemptId, false, "queue_full");
        }

        _logger.LogInformation(
            "Inactivity attempt queued AttemptId={AttemptId} Outcome={Outcome} HasImage={HasImage} Pending={Pending}",
            attemptId, attempt.Outcome, spoolPath is not null, _buffer.Count);

        return new EvidenceTransferAckPayload(attemptId, true, null);
    }

    private string? ValidatePolicy(InactivityCaptureAttemptPayload attempt)
    {
        var policy = _policyCache.Current;
        if (!policy.ActivitySignalEnabled)
            return "activity_signal_disabled";

        if (!policy.InactivityScreenshotEnabled)
            return "inactivity_screenshot_disabled";

        if (attempt.Outcome == InactivityCaptureOutcomes.Captured
            && (!policy.ScreenshotEnabled || !policy.InactivityScreenshotEnabled))
            return "screenshot_disabled";

        return null;
    }
}
