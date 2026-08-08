namespace ONEVO.Agent.Service;

using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
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
        DeviceIdentityStore deviceIdentityStore)
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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyDevBootstrapIfConfigured();

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
            ClearStoredCredentials();
            return;
        }

        PersistAuth(identity, result.Auth);
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

    private void ClearStoredCredentials()
    {
        _credentials.ClearDeviceJwt();
        _credentials.ClearRefreshToken();
        _deviceIdentityStore.Clear();
    }

    private void ApplyEnrollmentGates()
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
        }
    }

    private async Task HandleLifecycleCommandAsync(
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
                snap.ScheduleDisplay);
            _logger.LogInformation(
                "Session saved to SQLite Work={Work} Break={Break} Breaks={Count} Db={Db}",
                snap.AccumulatedWork, snap.AccumulatedBreak, snap.BreakSessionCount,
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

    private async Task HandleCollectionSubmitAsync(
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

        if (!_policyCache.Current.ActivitySignalEnabled)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CollectionRecordAck,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CollectionRecordAckPayload
                    {
                        AcceptedCount = 0,
                        ErrorCode = "activity_signal_disabled"
                    })
            });
            return;
        }

        var accepted = 0;
        foreach (var record in payload.Records)
        {
            if (record.RecordType is not (CollectionRecordTypes.ActivitySnapshot
                or CollectionRecordTypes.AppUsageSnapshot
                or CollectionRecordTypes.DeviceStateSnapshot
                or CollectionRecordTypes.Screenshot
                or CollectionRecordTypes.FacePhoto))
                continue;

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
    }

    private async Task HandleActivationCodeSubmitAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<ActivationCodeSubmitPayload>();
        var code = payload?.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length < 6)
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "INVALID_CODE", null);
            return;
        }

        var current = _stateMachine.CurrentState;
        if (current is MonitoringState.Locked)
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "LOCKED", null);
            return;
        }

        if (current is MonitoringState.Stopped or MonitoringState.Active or MonitoringState.Paused)
        {
            // Already enrolled — treat as success so tray can continue.
            await ReplyEnrollmentAsync(envelope, reply, true, null, null);
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

        var (deviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(result.Auth.AccessToken);
        var identity = new DeviceIdentity
        {
            // Backend has no separate "AgentId" concept yet for a tray device — reuse
            // DeviceId until/unless the backend response grows a distinct field.
            DeviceId = deviceId ?? Guid.NewGuid().ToString("N"),
            AgentId = deviceId ?? Guid.NewGuid().ToString("N"),
            TenantId = tenantId ?? string.Empty,
            DeviceFingerprint = fingerprint
        };

        PersistAuth(identity, result.Auth);

        if (!_stateMachine.TryTransition(MonitoringState.Stopped, out _))
        {
            await ReplyEnrollmentAsync(envelope, reply, false, "INVALID_STATE", null);
            return;
        }

        ApplyEnrollmentGates();

        _logger.LogInformation("Activation succeeded via backend exchange. State={State}", _stateMachine.CurrentState);

        await ReplyEnrollmentAsync(
            envelope, reply, true, null,
            result.Auth.EmployeeName, result.Auth.EmployeeEmail, result.Auth.EmployeeNumber);

        // Push status so tray coordinator sees Stopped (enrolled) not Unenrolled.
        await reply(BuildStatusEnvelope(correlationId: null));
    }

    private async Task ReplyEnrollmentAsync(
        IpcEnvelope request,
        Func<IpcEnvelope, Task> reply,
        bool success,
        string? errorCode,
        string? employeeName,
        string? employeeEmail = null,
        string? employeeNumber = null)
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
                EmployeeNumber = employeeNumber
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
        ClearStoredCredentials();
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pipeServer.MessageReceived -= HandleMessageAsync;
        await _pipeServer.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
