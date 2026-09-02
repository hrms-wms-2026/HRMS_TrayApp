namespace ONEVO.Agent.Service;

using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly NamedPipeServer _pipeServer;
    private readonly AgentStateMachine _stateMachine;
    private readonly PolicyCache _policyCache;
    private readonly ActivityRecordBuffer _activityBuffer;
    private readonly PresenceSession _presenceSession;
    private readonly LifecycleGate _lifecycleGate;
    private readonly AgentOptions _options;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly DeviceIdentityStore _deviceIdentityStore;
    private readonly EnrollmentCoordinator _enrollmentCoordinator;
    private readonly InactivityEvidenceHandler _inactivityEvidence;
    private readonly EvidenceSpoolStore _evidenceSpool;
    private CancellationTokenSource? _pairingCts;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        NamedPipeServer pipeServer,
        AgentStateMachine stateMachine,
        PolicyCache policyCache,
        ActivityRecordBuffer activityBuffer,
        PresenceSession presenceSession,
        LifecycleGate lifecycleGate,
        IOptions<AgentOptions> options,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        DeviceIdentityStore deviceIdentityStore,
        EnrollmentCoordinator enrollmentCoordinator,
        InactivityEvidenceHandler inactivityEvidence,
        EvidenceSpoolStore evidenceSpool)
    {
        _logger = logger;
        _pipeServer = pipeServer;
        _stateMachine = stateMachine;
        _policyCache = policyCache;
        _activityBuffer = activityBuffer;
        _presenceSession = presenceSession;
        _lifecycleGate = lifecycleGate;
        _options = options.Value;
        _apiClient = apiClient;
        _credentials = credentials;
        _deviceIdentityStore = deviceIdentityStore;
        _enrollmentCoordinator = enrollmentCoordinator;
        _inactivityEvidence = inactivityEvidence;
        _evidenceSpool = evidenceSpool;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyDevBootstrapIfConfigured();

        _evidenceSpool.PurgeExpired(DateTimeOffset.UtcNow);

        _presenceSession.SetScheduleDisplay(_options.DefaultScheduleDisplay);

        if (_stateMachine.CurrentState == MonitoringState.Unenrolled)
            await TryResumeSessionAsync(stoppingToken);

        _pipeServer.MessageReceived += HandleMessageAsync;
        await _pipeServer.StartAsync(stoppingToken);
        _logger.LogInformation("ONEVO Agent Service ready. State: {State}", _stateMachine.CurrentState);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// "Login": if a device identity and refresh token survived a restart, resume
    /// the session silently instead of showing the connect screen again (§9).
    /// </summary>
    private async Task TryResumeSessionAsync(CancellationToken ct)
    {
        var identity = _deviceIdentityStore.Load();
        var refreshToken = _credentials.ReadRefreshToken();
        if (identity is null || string.IsNullOrWhiteSpace(refreshToken))
            return;

        var result = await _apiClient.RefreshTokenAsync(refreshToken, identity.DeviceFingerprint, ct);
        if (!result.Success || result.Auth is null)
        {
            _logger.LogWarning(
                "Silent session resume failed ({ErrorCode}) — clearing stored credentials", result.ErrorCode);
            ClearStoredAuth();
            return;
        }

        PersistAuth(identity, result.Auth);
        await _apiClient.SendHeartbeatAsync(result.Auth.AccessToken, ct);
        ApplyEnrollmentGates();
        _stateMachine.TryTransition(MonitoringState.Stopped, out _);
        _logger.LogInformation("Session resumed silently on startup. State={State}", _stateMachine.CurrentState);
    }

    private void PersistAuth(DeviceIdentity identity, TrayAuthPayload auth)
    {
        // Write the new refresh token before anything else — if the process dies
        // mid-refresh, the worst case is re-using a token the backend already
        // rotated away, not losing the credential entirely.
        _credentials.StoreRefreshToken(auth.RefreshToken);
        _credentials.StoreDeviceJwt(auth.AccessToken);
        _deviceIdentityStore.Save(identity);
    }

    /// <summary>
    /// Shared tail for both connect paths (manual code exchange and browser device
    /// pairing) once a TrayAuthPayload has been obtained: derives/persists device
    /// identity, sends the initial heartbeat, and transitions into Stopped
    /// (enrolled-but-not-clocked-in). Returns false with "INVALID_STATE" only if the
    /// state machine transition itself is rejected (e.g. a race with a concurrent
    /// enrollment) — everything before that point cannot fail once a valid auth
    /// payload is in hand.
    /// </summary>
    private async Task<(bool Success, string? ErrorCode)> CompleteEnrollmentAsync(
        TrayAuthPayload auth, string fingerprint, CancellationToken ct)
    {
        var (backendDeviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(auth.AccessToken);
        var storedIdentity = _deviceIdentityStore.Load();
        var stableDeviceId = storedIdentity?.DeviceId
            ?? backendDeviceId
            ?? Guid.NewGuid().ToString("N");
        var stableAgentId = storedIdentity?.AgentId
            ?? backendDeviceId
            ?? stableDeviceId;
        var identity = new DeviceIdentity
        {
            DeviceId = stableDeviceId,
            AgentId = stableAgentId,
            TenantId = tenantId ?? storedIdentity?.TenantId ?? string.Empty,
            DeviceFingerprint = fingerprint,
            TenantSlug = auth.TenantSlug ?? storedIdentity?.TenantSlug
        };

        PersistAuth(identity, auth);
        await _apiClient.SendHeartbeatAsync(auth.AccessToken, ct);

        if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
            return (false, "INVALID_STATE");

        ApplyEnrollmentGates();
        return (true, null);
    }

    internal MonitoringState CurrentStateForTest => _stateMachine.CurrentState;

    /// <summary>
    /// Clears authentication material without deleting the machine identity.
    /// DeviceId is installation-scoped and must remain stable across sign-out.
    /// </summary>
    private void ClearStoredAuth()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
    }

    /// <summary>
    /// Internal (not private) so tests can exercise the fixed set of gates a successful
    /// enrollment/session-resume applies without going through the full activation-code/HTTP
    /// flow — mirrors the reasoning documented on <see cref="HandleCollectionSubmitAsync"/>.
    /// </summary>
    internal void ApplyEnrollmentGates()
    {
        _lifecycleGate.SetDeviceEnrolled(true);
        _lifecycleGate.SetCredentialValid(true);
        _lifecycleGate.SetDeviceApproved(true);

        // Consent capture and server policy-fetch are not yet built (§23 gap) — until
        // they exist, a successful backend-verified login is the strongest signal we
        // have, so these stay true post-enrollment same as before. Replace with real
        // sources once those features land; do not silently regress Clock In in the
        // meantime by leaving them false.
        _lifecycleGate.SetEmployeeSessionActive(true);
        _lifecycleGate.SetConsentValid(true);
        _lifecycleGate.SetPolicyAllowsCollection(true);
        _lifecycleGate.SetNotOnApprovedTimeOff(true);
    }

    private void ApplyDevBootstrapIfConfigured()
    {
        // Prefer lifecycle-driven Active over auto-force so Clock In UI works.
        if (_options.AllowLocalLifecycleWithoutFullGates)
        {
            // Ensure we can clock in from Stopped (leave Unenrolled → Stopped for demo).
            if (_stateMachine.CurrentState == MonitoringState.Unenrolled)
                _stateMachine.TryTransition(MonitoringState.Stopped, out _);

            _lifecycleGate.SetDeviceEnrolled(true);
            _lifecycleGate.SetCredentialValid(true);
            _lifecycleGate.SetDeviceApproved(true);
            _lifecycleGate.SetEmployeeSessionActive(true);
            _lifecycleGate.SetConsentValid(true);
            _lifecycleGate.SetPolicyAllowsCollection(true);
            _lifecycleGate.SetNotOnApprovedTimeOff(true);
            // Presence + not-on-break set on ClockIn.
            _logger.LogWarning(
                "AllowLocalLifecycleWithoutFullGates=true — local ClockIn/Break/ClockOut enabled (development)");
            return;
        }

        if (!_options.ForceMonitoringActive)
            return;

        _stateMachine.TryTransition(MonitoringState.Stopped, out _);
        _stateMachine.TryTransition(MonitoringState.Active, out _);
        _logger.LogWarning(
            "ForceMonitoringActive=true — monitoring forced Active (development only)");
    }

    private async Task HandleMessageAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        _presenceSession.ObserveInbound(DateTimeOffset.UtcNow);

        switch (envelope.Type)
        {
            case IpcMessageTypes.StatusRequest:
                await ReplyStatusAndPolicyAsync(envelope, reply);
                break;

            case IpcMessageTypes.CollectionRecordSubmit:
                await HandleCollectionSubmitAsync(envelope, reply);
                break;

            case IpcMessageTypes.ActivationCodeSubmit:
                await HandleActivationCodeSubmitAsync(envelope, reply);
                break;

            case IpcMessageTypes.LifecycleCommand:
                await HandleLifecycleCommandAsync(envelope, reply);
                break;

            case IpcMessageTypes.LogoutRequest:
                await HandleLogoutRequestAsync(envelope, reply);
                break;

            case IpcMessageTypes.BiometricEnrollmentStart:
                await HandleBiometricEnrollmentStartAsync(envelope, reply);
                break;

            case IpcMessageTypes.BiometricEnrollmentCaptureFinished:
                await HandleBiometricEnrollmentCaptureFinishedAsync(envelope, reply);
                break;

            case IpcMessageTypes.DevicePairingStart:
                await HandleDevicePairingStartAsync(envelope, reply);
                break;

            case IpcMessageTypes.DevicePairingCancel:
                await HandleDevicePairingCancelAsync(envelope, reply);
                break;

            case IpcMessageTypes.EvidenceTransferStart:
                HandleEvidenceTransferStart(envelope);
                break;

            case IpcMessageTypes.EvidenceTransferChunk:
                HandleEvidenceTransferChunk(envelope);
                break;

            case IpcMessageTypes.EvidenceTransferComplete:
                await HandleEvidenceTransferCompleteAsync(envelope, reply);
                break;
        }
    }

    private void HandleEvidenceTransferStart(IpcEnvelope envelope)
    {
        var payload = envelope.Payload?.Deserialize<EvidenceTransferStartPayload>();
        if (payload is null) return;
        _inactivityEvidence.HandleStart(payload, DateTimeOffset.UtcNow);
    }

    private void HandleEvidenceTransferChunk(IpcEnvelope envelope)
    {
        var payload = envelope.Payload?.Deserialize<EvidenceTransferChunkPayload>();
        if (payload is null) return;
        _inactivityEvidence.HandleChunk(payload, DateTimeOffset.UtcNow);
    }

    private async Task HandleEvidenceTransferCompleteAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<EvidenceTransferCompletePayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.EvidenceTransferAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new EvidenceTransferAckPayload(Guid.Empty, false, "invalid_payload"))
            });
            return;
        }

        var ack = _inactivityEvidence.HandleComplete(payload.AttemptId, DateTimeOffset.UtcNow);
        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.EvidenceTransferAck,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(ack)
        });
    }

    private async Task HandleBiometricEnrollmentStartAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var result = await _enrollmentCoordinator.StartAsync(CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.BiometricEnrollmentSessionReady,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new BiometricEnrollmentSessionReadyPayload(
                result.Success, result.ErrorCode, result.AttemptId, result.AwsSessionId, result.Region,
                result.ChallengeType, result.AccessKeyId, result.SecretAccessKey, result.SessionToken,
                result.CredentialsExpireAt))
        });
    }

    private async Task HandleBiometricEnrollmentCaptureFinishedAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<BiometricEnrollmentCaptureFinishedPayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.BiometricEnrollmentResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new BiometricEnrollmentResultPayload(false, "INVALID_PAYLOAD", null))
            });
            return;
        }

        // The backend re-derives the verdict from AWS regardless of CaptureSucceeded — the Tray's
        // local capture outcome is only used for logging/UX, never trusted as the security decision.
        var result = await _enrollmentCoordinator.CompleteAsync(payload.AttemptId, CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.BiometricEnrollmentResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new BiometricEnrollmentResultPayload(result.Success, result.ErrorCode, result.ProfileStatus))
        });
    }

    /// <summary>
    /// Internal (not private) so tests can submit ClockIn/StartBreak/EndBreak/ClockOut lifecycle
    /// commands directly — mirrors the reasoning documented on
    /// <see cref="HandleCollectionSubmitAsync"/>.
    /// </summary>
    internal async Task HandleLifecycleCommandAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<LifecycleCommandPayload>();
        if (payload is null)
        {
            await ReplyLifecycleAsync(envelope, reply, false, "INVALID_PAYLOAD",
                "Missing lifecycle payload.", _stateMachine.CurrentState);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (success, errorCode, message, state) = payload.Action switch
        {
            LifecycleAction.ClockIn    => ExecuteClockIn(now),
            LifecycleAction.StartBreak => ExecuteStartBreak(now),
            LifecycleAction.EndBreak   => ExecuteEndBreak(now),
            LifecycleAction.ClockOut   => ExecuteClockOut(now),
            _ => (false, "UNKNOWN_ACTION", "Unknown lifecycle action.", _stateMachine.CurrentState)
        };

        _logger.LogInformation(
            "Lifecycle {Action} Success={Success} Error={Error} State={State}",
            payload.Action, success, errorCode ?? "-", state);

        await ReplyLifecycleAsync(envelope, reply, success, errorCode, message, state);

        // Also push a status snapshot so tray navigates even if it only listens to StatusResponse.
        await reply(BuildStatusEnvelope(correlationId: null));
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockIn(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current == MonitoringState.Active)
            return (false, "ALREADY_CLOCKED_IN", "You are already clocked in.", current);
        if (current == MonitoringState.Paused)
            return (false, "ON_BREAK", "End break or clock out first.", current);
        if (current == MonitoringState.Locked)
            return (false, "LOCKED", "Agent is locked. Re-enrollment required.", current);
        if (current == MonitoringState.Unenrolled)
            return (false, "UNENROLLED", "Device is not enrolled.", current);

        // Presence session must be active before CanActivate is true.
        _lifecycleGate.SetPresenceSessionActive(true);
        _lifecycleGate.SetNotOnBreak(true);

        if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
        {
            _lifecycleGate.SetPresenceSessionActive(false);
            return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);
        }

        if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
            return (false, "INVALID_STATE", $"Cannot clock in from {current}.", current);

        _presenceSession.ClockIn(now);
        return (true, null, "Clocked in successfully.", MonitoringState.Active);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteStartBreak(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current != MonitoringState.Active)
            return (false, "INVALID_STATE", "Break is only available while working.", current);

        if (!_stateMachine.TryTransition(MonitoringState.Paused, out _))
            return (false, "INVALID_STATE", "Cannot start break.", current);

        _lifecycleGate.SetNotOnBreak(false);
        _presenceSession.StartBreak(now);
        return (true, null, "Break started.", MonitoringState.Paused);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteEndBreak(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current != MonitoringState.Paused)
            return (false, "INVALID_STATE", "You are not on a break.", current);

        _lifecycleGate.SetNotOnBreak(true);

        if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
        {
            // Keep paused if gates fail.
            _lifecycleGate.SetNotOnBreak(false);
            return (false, "GATES_CLOSED", "Cannot resume — gates not satisfied.", current);
        }

        if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
            return (false, "INVALID_STATE", "Cannot end break.", current);

        _presenceSession.EndBreak(now);
        return (true, null, "Break ended. Welcome back.", MonitoringState.Active);
    }

    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockOut(
        DateTimeOffset now)
    {
        var current = _stateMachine.CurrentState;
        if (current is not (MonitoringState.Active or MonitoringState.Paused))
            return (false, "INVALID_STATE", "You are not in an active work session.", current);

        if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
            return (false, "INVALID_STATE", "Cannot clock out.", current);

        _presenceSession.ClockOut(now);
        _lifecycleGate.SetPresenceSessionActive(false);
        _lifecycleGate.SetNotOnBreak(true);

        // Durable SQL history for completed day (local audit copy).
        try
        {
            var snap = _presenceSession.Snapshot(now);
            _activityBuffer.SaveSessionHistory(
                snap.ClockInAt,
                snap.ClockOutAt,
                snap.AccumulatedBreak,
                snap.AccumulatedWork,
                snap.BreakSessionCount,
                snap.ScheduleDisplay,
                snap.AccumulatedIdle);
            _logger.LogInformation(
                "Session saved to SQLite Work={Work} Break={Break} Idle={Idle} Breaks={Count} Db={Db}",
                snap.AccumulatedWork, snap.AccumulatedBreak, snap.AccumulatedIdle, snap.BreakSessionCount,
                _activityBuffer.DatabasePath);

            EnqueueWorkSessionSync(snap);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist session_history to SQLite");
        }

        return (true, null, "Clocked out. Workday completed.", MonitoringState.Stopped);
    }

    /// <summary>
    /// Queues the completed session onto the same durable buffer used for activity/app-usage/
    /// device-state records, so it gets the existing offline-safe retry and ordering for free
    /// (§11) instead of a bespoke sync path. SessionId doubles as the CollectionRecord EventId —
    /// the backend upserts on it, so a retried delivery is a no-op, never a duplicate row.
    /// </summary>
    private void EnqueueWorkSessionSync(SessionSnapshot snap)
    {
        if (snap.ClockInAt is null || snap.ClockOutAt is null)
            return;

        var sessionId = _presenceSession.CurrentSessionId;
        var payload = new WorkSessionPayload
        {
            SessionId = sessionId,
            ClockInAt = snap.ClockInAt.Value,
            ClockOutAt = snap.ClockOutAt.Value,
            AccumulatedBreak = snap.AccumulatedBreak,
            AccumulatedWork = snap.AccumulatedWork,
            AccumulatedIdle = snap.AccumulatedIdle,
            BreakSessionCount = snap.BreakSessionCount,
            ScheduleDisplay = snap.ScheduleDisplay
        };

        var record = new CollectionRecord
        {
            EventId = sessionId.ToString("N"),
            RecordType = CollectionRecordTypes.WorkSession,
            SchemaVersion = CollectionSchemaVersions.WorkSessionV1,
            CaptureTimestamp = snap.ClockOutAt.Value,
            DeviceId = _deviceIdentityStore.Load()?.DeviceId ?? "unknown",
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        if (_activityBuffer.TryEnqueue(record))
            _logger.LogInformation("Work session queued for backend sync SessionId={SessionId}", sessionId);
        else
            _logger.LogWarning("Activity buffer full — dropping work session SessionId={SessionId}", sessionId);
    }

    private async Task ReplyLifecycleAsync(
        IpcEnvelope request,
        Func<IpcEnvelope, Task> reply,
        bool success,
        string? errorCode,
        string? message,
        MonitoringState state)
    {
        var result = new LifecycleResultPayload(
            Success: success,
            ErrorCode: errorCode,
            Message: message,
            State: state,
            Session: _presenceSession.Snapshot(DateTimeOffset.UtcNow));

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.LifecycleResult,
            CorrelationId = request.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(result)
        });
    }

    private async Task ReplyStatusAndPolicyAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        await reply(BuildStatusEnvelope(envelope.CorrelationId));

        var policy = new IpcEnvelope
        {
            Type = IpcMessageTypes.PolicyPush,
            Payload = JsonSerializer.SerializeToElement(
                new PolicyPushPayload { Policy = _policyCache.Current })
        };
        await reply(policy);
    }

    private IpcEnvelope BuildStatusEnvelope(string? correlationId) =>
        new()
        {
            Type = IpcMessageTypes.StatusResponse,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.SerializeToElement(
                new StatusResponsePayload(
                    _stateMachine.CurrentState,
                    DateTimeOffset.UtcNow,
                    _presenceSession.Snapshot(DateTimeOffset.UtcNow)))
        };

    /// <summary>
    /// Internal (not private) so AgentWorkerCollectionSubmitTests can assert the per-record-type
    /// policy gate directly — mirrors the convention used by
    /// <see cref="Sync.PolicySyncService.RefreshInterval"/> for the same reason.
    /// </summary>
    internal async Task HandleCollectionSubmitAsync(
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<CollectionRecordSubmitPayload>();
        if (payload?.Records is null || payload.Records.Count == 0)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload { AcceptedCount = 0, ErrorCode = "empty" })
            });
            return;
        }

        // Only accept collection while Active (lifecycle gate).
        if (_stateMachine.CurrentState != MonitoringState.Active)
        {
            _logger.LogInformation(
                "Rejected collection submit while state={State} count={Count}",
                _stateMachine.CurrentState,
                payload.Records.Count);
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload
                    {
                        AcceptedCount = 0,
                        ErrorCode = "monitoring_not_active"
                    })
            });
            return;
        }

        var currentPolicy = _policyCache.Current;
        var accepted = 0;
        var idleChanged = false;
        var stableDeviceId = _deviceIdentityStore.Load()?.DeviceId ?? "unknown";
        foreach (var incomingRecord in payload.Records)
        {
            // The tray is not trusted to choose a device identifier (older builds
            // used Environment.MachineName for face photos). Normalize all records
            // at the service boundary to the installation-scoped identity.
            var record = incomingRecord with { DeviceId = stableDeviceId };
            if (record.RecordType is not (CollectionRecordTypes.ActivitySnapshot
                or CollectionRecordTypes.AppUsageSnapshot
                or CollectionRecordTypes.DeviceStateSnapshot
                or CollectionRecordTypes.Screenshot
                or CollectionRecordTypes.FacePhoto))
                continue;

            // Per-record-type policy gate at ingest — mirrors ActivitySyncService.IsAllowedByPolicy
            // exactly (Screenshot->ScreenshotEnabled, AppUsage->AppUsageEnabled,
            // FacePhoto->CameraVerificationEnabled, Activity/DeviceState->ActivitySignalEnabled) so a
            // capability disabled server-side is rejected here rather than buffered and only
            // dropped later at ActivitySyncService flush time.
            if (!ActivitySyncService.IsAllowedByPolicy(record.RecordType, currentPolicy))
            {
                _logger.LogInformation(
                    "Rejected {RecordType} at ingest — capability disabled by policy", record.RecordType);
                continue;
            }

            if (record.RecordType == CollectionRecordTypes.DeviceStateSnapshot)
            {
                try
                {
                    var device = record.Payload.Deserialize<DeviceStateSnapshotPayload>();
                    if (device is not null)
                        idleChanged |= _presenceSession.ApplyDeviceStateIdle(device);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "DeviceState payload could not be applied to presence idle");
                }
            }

            if (_activityBuffer.TryEnqueue(record))
                accepted++;
            else
                _logger.LogWarning("Activity buffer full — dropping eventId={EventId}", record.EventId);
        }

        _logger.LogInformation(
            "SQLite buffered records Accepted={Accepted} Pending={Pending} TotalStored={Total} Db={Db}",
            accepted,
            _activityBuffer.Count,
            _activityBuffer.TotalStoredCount,
            _activityBuffer.DatabasePath);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.CollectionRecordAck,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new CollectionRecordAckPayload { AcceptedCount = accepted })
        });

        if (idleChanged)
        {
            try
            {
                await _pipeServer.BroadcastAsync(BuildStatusEnvelope(correlationId: null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast status after idle change");
            }
        }
    }

    private async Task HandleActivationCodeSubmitAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<ActivationCodeSubmitPayload>();
        var code = payload?.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!IsValidActivationCode(code))
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "INVALID_CODE", null);
            return;
        }

        var current = _stateMachine.CurrentState;
        if (current is MonitoringState.Locked
            && !string.IsNullOrWhiteSpace(_credentials.ReadRefreshToken()))
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "LOCKED", null);
            return;
        }

        if (current is MonitoringState.Stopped or MonitoringState.Active or MonitoringState.Paused)
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "ALREADY_ENROLLED", null);
            return;
        }

        var fingerprint = DeviceFingerprint.Compute();
        var result = await _apiClient.ExchangeActivationCodeAsync(
            code, Environment.MachineName, "Windows", fingerprint, CancellationToken.None);

        if (!result.Success || result.Auth is null)
        {
            var errorCode = result.ErrorCode == "UNAUTHORIZED" ? "INVALID_CODE" : result.ErrorCode;
            _logger.LogWarning("Activation exchange failed. ErrorCode={ErrorCode}", errorCode);
            await ReplyEnrollmentAsync(envelope, reply, false, errorCode, null);
            return;
        }

        var (completed, completionError) = await CompleteEnrollmentAsync(result.Auth, fingerprint, CancellationToken.None);
        if (!completed)
        {
            await ReplyEnrollmentAsync(envelope, reply, false, completionError, null);
            return;
        }

        _logger.LogInformation("Activation succeeded via backend exchange. State={State}", _stateMachine.CurrentState);

        await ReplyEnrollmentAsync(
            envelope, reply, true, null,
            result.Auth.EmployeeName, result.Auth.EmployeeEmail, result.Auth.EmployeeNumber,
            result.Auth.EmployeeProfileStatus,
            result.Auth.DepartmentName, result.Auth.WorkModeLabel, result.Auth.OfficeName,
            result.Auth.OrganizationName);

        // Push status so tray coordinator sees Stopped (enrolled) not Unenrolled.
        await reply(BuildStatusEnvelope(correlationId: null));
    }

    internal async Task HandleDevicePairingStartAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<DevicePairingStartPayload>();
        if (payload is null)
        {
            await reply(BuildDevicePairingStartedEnvelope(envelope.CorrelationId, false, "INVALID_REQUEST"));
            return;
        }

        var fingerprint = DeviceFingerprint.Compute();
        var start = await _apiClient.StartDeviceAuthorizationAsync(
            payload.DeviceName, payload.DeviceOs, payload.ClientVersion, fingerprint, CancellationToken.None);

        if (!start.Success || start.DeviceCode is null)
        {
            _logger.LogWarning("Device pairing start failed. ErrorCode={ErrorCode}", start.ErrorCode);
            await reply(BuildDevicePairingStartedEnvelope(envelope.CorrelationId, false, start.ErrorCode ?? "SERVICE_UNAVAILABLE"));
            return;
        }

        var knownTenantSlug = _deviceIdentityStore.Load()?.TenantSlug;

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.DevicePairingStarted,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartedPayload(
                true, null,
                ApplyKnownTenantSubdomain(start.VerificationUri, knownTenantSlug),
                ApplyKnownTenantSubdomain(start.VerificationUriComplete, knownTenantSlug),
                start.ExpiresInSeconds, start.IntervalSeconds))
        });

        _pairingCts?.Cancel();
        _pairingCts = new CancellationTokenSource();
        _ = PollDevicePairingLoopAsync(start, fingerprint, _pairingCts.Token);
    }

    /// <summary>
    /// The backend always builds verification URLs against its generic base host, which never
    /// carries a session (only a tenant subdomain does). If this device has previously connected
    /// and remembers which tenant it belongs to (persisted from a prior TrayAuthPayload.TenantSlug),
    /// prepending that slug as a subdomain lets the browser land somewhere the user may already be
    /// signed in, skipping the base-host login detour. A device that has never connected has no
    /// remembered slug, so this is a no-op and the base-host flow (log in once, backend resolves
    /// the real tenant) runs as normal — that first-time login is unavoidable, not a bug.
    /// </summary>
    private static string? ApplyKnownTenantSubdomain(string? url, string? tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(tenantSlug))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var builder = new UriBuilder(uri) { Host = $"{tenantSlug}.{uri.Host}" };
        return builder.Uri.ToString();
    }

    private static IpcEnvelope BuildDevicePairingStartedEnvelope(string correlationId, bool success, string? errorCode) =>
        new()
        {
            Type = IpcMessageTypes.DevicePairingStarted,
            CorrelationId = correlationId,
            Payload = JsonSerializer.SerializeToElement(new DevicePairingStartedPayload(success, errorCode))
        };

    internal Task HandleDevicePairingCancelAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        _pairingCts?.Cancel();
        _pairingCts = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls PollDeviceAuthorizationAsync at the server-specified interval until a terminal
    /// state is reached (Authorized, ExpiredToken, AccessDenied, ServiceUnavailable) or the
    /// authorization's own expiry deadline passes, then pushes exactly one
    /// DevicePairingResult. On Authorized, runs the same completion tail
    /// HandleActivationCodeSubmitAsync uses. <paramref name="delay"/> and
    /// <paramref name="pushResult"/> are test seams — production callers omit both and get
    /// real Task.Delay plus a broadcast over the named pipe.
    /// </summary>
    internal async Task PollDevicePairingLoopAsync(
        DeviceAuthorizationStartResult start,
        string fingerprint,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DevicePairingResultPayload, Task>? pushResult = null)
    {
        delay ??= Task.Delay;
        pushResult ??= PushDevicePairingResultAsync;

        var interval = TimeSpan.FromSeconds(start.IntervalSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(start.ExpiresInSeconds);

        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            await delay(interval, ct);
            if (ct.IsCancellationRequested) return;

            var poll = await _apiClient.PollDeviceAuthorizationAsync(start.DeviceCode!, fingerprint, ct);

            switch (poll.State)
            {
                case DeviceAuthorizationPollState.AuthorizationPending:
                    continue;

                case DeviceAuthorizationPollState.SlowDown:
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case DeviceAuthorizationPollState.Authorized:
                {
                    var (completed, completionError) = await CompleteEnrollmentAsync(poll.Auth!, fingerprint, ct);
                    if (!completed)
                    {
                        await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = completionError });
                        return;
                    }
                    _logger.LogInformation("Device pairing succeeded via browser approval. State={State}", _stateMachine.CurrentState);
                    await pushResult(new DevicePairingResultPayload
                    {
                        Success = true,
                        EmployeeName = poll.Auth!.EmployeeName,
                        EmployeeEmail = poll.Auth.EmployeeEmail,
                        EmployeeNumber = poll.Auth.EmployeeNumber,
                        EmployeeProfileStatus = poll.Auth.EmployeeProfileStatus,
                        DepartmentName = poll.Auth.DepartmentName,
                        WorkModeLabel = poll.Auth.WorkModeLabel,
                        OfficeName = poll.Auth.OfficeName,
                        OrganizationName = poll.Auth.OrganizationName
                    });
                    return;
                }

                case DeviceAuthorizationPollState.ExpiredToken:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "EXPIRED" });
                    return;

                case DeviceAuthorizationPollState.AccessDenied:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "ACCESS_DENIED" });
                    return;

                case DeviceAuthorizationPollState.ServiceUnavailable:
                    await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "SERVICE_UNAVAILABLE" });
                    return;
            }
        }

        await pushResult(new DevicePairingResultPayload { Success = false, ErrorCode = "EXPIRED" });
    }

    private async Task PushDevicePairingResultAsync(DevicePairingResultPayload payload)
    {
        try
        {
            await _pipeServer.BroadcastAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.DevicePairingResult,
                Payload = JsonSerializer.SerializeToElement(payload)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast device pairing result");
        }
    }

    private async Task ReplyEnrollmentAsync(
        IpcEnvelope request,
        Func<IpcEnvelope, Task> reply,
        bool success,
        string? errorCode,
        string? employeeName,
        string? employeeEmail = null,
        string? employeeNumber = null,
        string? employeeProfileStatus = null,
        string? departmentName = null,
        string? workModeLabel = null,
        string? officeName = null,
        string? organizationName = null)
    {
        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.EnrollmentResult,
            CorrelationId = request.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new EnrollmentResultPayload
            {
                Success = success,
                ErrorCode = errorCode,
                EmployeeName = employeeName,
                EmployeeEmail = employeeEmail,
                EmployeeNumber = employeeNumber,
                EmployeeProfileStatus = employeeProfileStatus,
                DepartmentName = departmentName,
                WorkModeLabel = workModeLabel,
                OfficeName = officeName,
                OrganizationName = organizationName
            })
        });
    }

    private async Task HandleLogoutRequestAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var current = _stateMachine.CurrentState;

        // Never jump straight from Active/Paused to Unenrolled — stop collection first.
        if (current is MonitoringState.Active or MonitoringState.Paused)
            _stateMachine.TryTransition(MonitoringState.Stopped, out _);

        var accessToken = _credentials.ReadDeviceJwt();
        if (!string.IsNullOrWhiteSpace(accessToken))
            await _apiClient.RevokeDeviceAsync(accessToken, CancellationToken.None);

        // Best-effort: the employee is leaving either way, so clear local state
        // regardless of whether the revoke call reached the backend.
        ClearStoredAuth();
        _lifecycleGate.SetDeviceEnrolled(false);
        _lifecycleGate.SetCredentialValid(false);
        _lifecycleGate.SetDeviceApproved(false);

        var success = _stateMachine.TryTransition(MonitoringState.Unenrolled, out _);
        _logger.LogInformation("Logout processed. Success={Success} State={State}", success, _stateMachine.CurrentState);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.LogoutResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new LogoutResultPayload(success, success ? null : "INVALID_STATE"))
        });

        await reply(BuildStatusEnvelope(correlationId: null));
    }

    private static bool IsValidActivationCode(string code)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return code.Length == 8 && code.All(alphabet.Contains);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pipeServer.MessageReceived -= HandleMessageAsync;
        await _pipeServer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
