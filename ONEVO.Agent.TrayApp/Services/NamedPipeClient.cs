namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class NamedPipeClient : INamedPipeClient, IAsyncDisposable
{
    private readonly ILogger<NamedPipeClient> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pending = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<EvidenceTransferAckPayload?>> _pendingEvidenceAcks = new();
    private readonly EvidenceTransferClient _evidenceTransferClient;
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    // Cleared without the write lock by ResetConnectionAsync; WriteEnvelopeAsync reads it once.
    private volatile StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _connectLoop;

    // CRITICAL: all collectors MUST stop on this event (§2.3)
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;
    public event Action<NotificationPushPayload>? OnNotificationReceived;
    public event Action<DevicePairingResultPayload>? OnDevicePairingResult;

    public StatusResponsePayload? LastKnownStatus { get; private set; }
    public AgentPolicy? LastKnownPolicy { get; private set; }

    public NamedPipeClient(ILogger<NamedPipeClient> logger) : this(logger, Constants.PipeName) { }

    // Tests pass a unique pipe name so they never touch a real Agent Service's pipe.
    internal NamedPipeClient(ILogger<NamedPipeClient> logger, string pipeName)
    {
        _logger = logger;
        _pipeName = pipeName;
        _evidenceTransferClient = new EvidenceTransferClient(WriteEnvelopeAsync, WaitForEvidenceAckAsync);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _connectLoop = ConnectWithRetryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        // Outer loop: keeps the client alive for the lifetime of the app. Each iteration runs one
        // exponential-backoff burst; if the burst can't connect it falls back to a slow fixed-interval
        // retry (Constants.ReconnectSteadyStateDelayMs) instead of giving up — the Service
        // restarting/recovering minutes later must not require a manual Tray App relaunch to be noticed.
        var disconnectedSignalled = false;

        while (!ct.IsCancellationRequested)
        {
            var connected = false;

            for (int attempt = 0; attempt < Constants.ReconnectMaxAttempts && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(
                        ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(Constants.IpcConnectionTimeoutMs, ct);
                    await AuthenticateAsync(_pipe, ct);

                    var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    _writer = new StreamWriter(_pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                    _logger.LogInformation("Connected to ONEVO Agent Service");
                    connected = true;
                    disconnectedSignalled = false;

                    await RequestStatusAsync(ct);
                    // Blocks until disconnected; resets the connection and invokes OnDisconnected itself.
                    await ReadLoopAsync(_pipe, utf8, ct);
                    disconnectedSignalled = true;
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning("Connect attempt {Attempt}/{Max} failed: {Msg}",
                        attempt + 1, Constants.ReconnectMaxAttempts, ex.Message);
                    await ResetConnectionAsync();

                    if (attempt < Constants.ReconnectMaxAttempts - 1)
                    {
                        var delayMs = Constants.ReconnectBaseDelayMs * (1 << attempt);
                        delayMs += Random.Shared.Next(0, 500);
                        await Task.Delay(delayMs, ct);
                    }
                }
            }

            if (ct.IsCancellationRequested) return;

            // Only fire OnDisconnected on the transition into "disconnected" — ReadLoopAsync already
            // signalled it if we lost a live connection; avoid a redundant call every steady-state retry.
            if (!connected && !disconnectedSignalled)
            {
                _logger.LogError("Failed to connect to Service after {Max} attempts; retrying every {Delay}ms",
                    Constants.ReconnectMaxAttempts, Constants.ReconnectSteadyStateDelayMs);
                disconnectedSignalled = true;
                OnDisconnected?.Invoke();
            }

            // A live connection that just dropped (Service restart/update) goes straight back into the
            // fast burst. The short pause stops a Service that accepts and then immediately closes
            // from turning this into a tight reconnect loop.
            var retryDelayMs = connected ? Constants.ReconnectBaseDelayMs : Constants.ReconnectSteadyStateDelayMs;
            try
            {
                await Task.Delay(retryDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Drops the current connection so writes fail fast (WriteEnvelopeAsync sees a null writer)
    // instead of throwing "Pipe is broken", and so the old pipe handle is not leaked on reconnect.
    private async Task ResetConnectionAsync()
    {
        _writer = null;
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _pipe?.Dispose();
            _pipe = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task AuthenticateAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Constants.IpcConnectionTimeoutMs);

        var line = await reader.ReadLineAsync(cts.Token)
            ?? throw new InvalidOperationException("Pipe closed during auth challenge");

        var challenge = JsonSerializer.Deserialize<IpcEnvelope>(line)
            ?? throw new InvalidOperationException("Invalid auth challenge envelope");

        if (challenge.Type != IpcMessageTypes.NonceChallenge)
            throw new InvalidOperationException($"Unexpected message type: {challenge.Type}");

        var payload = challenge.Payload?.Deserialize<NonceChallengePayload>()
            ?? throw new InvalidOperationException("Missing nonce in challenge");

        var response = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            CorrelationId = challenge.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(payload.Nonce))
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        await writer.FlushAsync(ct);
    }

    private async Task RequestStatusAsync(CancellationToken ct)
    {
        if (_writer is null) return;
        var request = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
        await WriteEnvelopeAsync(request, ct);
    }

    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) =>
        WriteEnvelopeAsync(envelope, ct);

    public Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct) =>
        SendRequestAsync<EnrollmentResultPayload>(
            IpcMessageTypes.ActivationCodeSubmit,
            JsonSerializer.SerializeToElement(new ActivationCodeSubmitPayload(code.Trim().ToUpperInvariant())),
            TimeSpan.FromSeconds(15), "Activation", ct);

    public Task<DevicePairingStartedPayload?> SendDevicePairingStartAsync(
        string deviceName, string deviceOs, string clientVersion, CancellationToken ct) =>
        SendRequestAsync<DevicePairingStartedPayload>(
            IpcMessageTypes.DevicePairingStart,
            JsonSerializer.SerializeToElement(new DevicePairingStartPayload(deviceName, deviceOs, clientVersion)),
            TimeSpan.FromSeconds(15), "Device pairing start", ct);

    public Task SendDevicePairingCancelAsync(CancellationToken ct) =>
        WriteEnvelopeAsync(new IpcEnvelope { Type = IpcMessageTypes.DevicePairingCancel }, ct);

    public Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct) =>
        SendRequestAsync<LogoutResultPayload>(
            IpcMessageTypes.LogoutRequest, null, TimeSpan.FromSeconds(15), "Logout", ct);

    public Task<UpdateCheckResultPayload?> SendUpdateCheckAsync(string currentVersion, CancellationToken ct) =>
        SendRequestAsync<UpdateCheckResultPayload>(
            IpcMessageTypes.UpdateCheckRequest,
            JsonSerializer.SerializeToElement(new UpdateCheckRequestPayload(currentVersion)),
            TimeSpan.FromSeconds(20), "Update check", ct);

    public Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null) =>
        SendRequestAsync<LifecycleResultPayload>(
            IpcMessageTypes.LifecycleCommand,
            JsonSerializer.SerializeToElement(new LifecycleCommandPayload(action, breakReason)),
            TimeSpan.FromSeconds(10), $"Lifecycle {action}", ct);

    public Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct) =>
        SendRequestAsync<BiometricEnrollmentSessionReadyPayload>(
            IpcMessageTypes.BiometricEnrollmentStart,
            JsonSerializer.SerializeToElement(new BiometricEnrollmentStartPayload()),
            TimeSpan.FromSeconds(15), "Biometric enrollment start", ct);

    public Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct) =>
        SendRequestAsync<BiometricEnrollmentResultPayload>(
            IpcMessageTypes.BiometricEnrollmentCaptureFinished,
            JsonSerializer.SerializeToElement(
                new BiometricEnrollmentCaptureFinishedPayload(attemptId, captureSucceeded, clientErrorCode)),
            TimeSpan.FromSeconds(30), "Biometric enrollment completion", ct);

    public Task<LocationChangeSubmitResultPayload?> SendLocationChangeSubmitAsync(
        double latitude, double longitude, double? accuracyMeters, string reason, CancellationToken ct) =>
        SendRequestAsync<LocationChangeSubmitResultPayload>(
            IpcMessageTypes.LocationChangeSubmit,
            JsonSerializer.SerializeToElement(
                new LocationChangeSubmitPayload(latitude, longitude, accuracyMeters, reason)),
            TimeSpan.FromSeconds(15), "Location change request submit", ct);

    public Task<LocationChangePendingResultPayload?> SendLocationChangePendingCheckAsync(CancellationToken ct) =>
        SendRequestAsync<LocationChangePendingResultPayload>(
            IpcMessageTypes.LocationChangePendingCheck,
            JsonSerializer.SerializeToElement(new LocationChangePendingCheckPayload()),
            TimeSpan.FromSeconds(10), "Location change pending check", ct);

    public Task<DailyTasksResultPayload?> SendDailyTasksRequestAsync(CancellationToken ct) =>
        SendRequestAsync<DailyTasksResultPayload>(
            IpcMessageTypes.DailyTasksRequest,
            JsonSerializer.SerializeToElement(new DailyTasksRequestPayload()),
            TimeSpan.FromSeconds(10), "Daily tasks request", ct);

    public Task<LocationChangeRespondResultPayload?> SendLocationChangeRespondAsync(
        Guid id, bool apply, CancellationToken ct) =>
        SendRequestAsync<LocationChangeRespondResultPayload>(
            IpcMessageTypes.LocationChangeRespond,
            JsonSerializer.SerializeToElement(new LocationChangeRespondPayload(id, apply)),
            TimeSpan.FromSeconds(15), "Location change respond", ct);

    public Task<WorkLocationConfirmResultPayload?> SendWorkLocationConfirmAsync(
        string locationType, double? latitude, double? longitude, double? accuracyMeters, CancellationToken ct) =>
        SendRequestAsync<WorkLocationConfirmResultPayload>(
            IpcMessageTypes.WorkLocationConfirm,
            JsonSerializer.SerializeToElement(
                new WorkLocationConfirmPayload(locationType, latitude, longitude, accuracyMeters)),
            TimeSpan.FromSeconds(10), "WorkLocationConfirm", ct);

    public Task<FacePhotoValidateResultPayload?> ValidateFacePhotoAsync(
        string format, byte[] jpegBytes, string purpose, CancellationToken ct,
        string? pose = null, Guid? enrollmentSessionId = null) =>
        SendRequestAsync<FacePhotoValidateResultPayload>(
            IpcMessageTypes.FacePhotoValidate,
            JsonSerializer.SerializeToElement(new FacePhotoValidatePayload(
                format, Convert.ToBase64String(jpegBytes), purpose, pose, enrollmentSessionId)),
            TimeSpan.FromSeconds(25), "Face photo validate", ct);

    // A Service built before these messages existed never replies, so keep the wait short:
    // setup then just shows the screens as before.
    public Task<PermissionConsentResultPayload?> GetPermissionConsentAsync(CancellationToken ct) =>
        SendRequestAsync<PermissionConsentResultPayload>(
            IpcMessageTypes.PermissionConsentStatus, null,
            TimeSpan.FromSeconds(8), IpcMessageTypes.PermissionConsentStatus, ct);

    public Task<PermissionConsentResultPayload?> SubmitPermissionConsentAsync(
        PermissionConsentPayload consent, CancellationToken ct) =>
        SendRequestAsync<PermissionConsentResultPayload>(
            IpcMessageTypes.PermissionConsentSubmit, JsonSerializer.SerializeToElement(consent),
            TimeSpan.FromSeconds(8), IpcMessageTypes.PermissionConsentSubmit, ct);

    public Task<FaceReferenceStatusResultPayload?> GetFaceReferenceStatusAsync(CancellationToken ct) =>
        SendRequestAsync<FaceReferenceStatusResultPayload>(
            IpcMessageTypes.FaceReferenceStatus, null, TimeSpan.FromSeconds(15), "Face reference status", ct);

    // Three DetectFaces, two CompareFaces and three uploads — slower than one preview.
    public Task<FaceEnrollCommitResultPayload?> CommitFaceEnrollmentAsync(
        Guid enrollmentSessionId, CancellationToken ct) =>
        SendRequestAsync<FaceEnrollCommitResultPayload>(
            IpcMessageTypes.FaceEnrollCommit,
            JsonSerializer.SerializeToElement(new FaceEnrollCommitPayload(enrollmentSessionId)),
            TimeSpan.FromSeconds(60), "Face setup commit", ct);

    public Task<LegalAcceptanceResultPayload?> SendLegalAcceptanceSubmitAsync(
        IReadOnlyList<LegalAcceptanceItemPayload> acceptances, CancellationToken ct) =>
        SendRequestAsync<LegalAcceptanceResultPayload>(
            IpcMessageTypes.LegalAcceptanceSubmit,
            JsonSerializer.SerializeToElement(new LegalAcceptanceSubmitPayload(acceptances)),
            TimeSpan.FromSeconds(10), "LegalAcceptanceSubmit", ct);

    /// <summary>
    /// Sends one request envelope and waits for the reply with the same correlation id. Returns
    /// null on timeout, on disconnect, or straight away when the pipe is not connected — no reply
    /// can arrive then, so the UI must not sit out the whole timeout behind a spinner.
    /// </summary>
    private async Task<TReply?> SendRequestAsync<TReply>(
        string type, JsonElement? payload, TimeSpan timeout, string label, CancellationToken ct)
        where TReply : class
    {
        var correlationId = Guid.NewGuid().ToString("N");
        // RunContinuationsAsynchronously keeps the caller's continuation off the pipe read loop.
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var sent = await WriteEnvelopeAsync(new IpcEnvelope
            {
                Type = type,
                CorrelationId = correlationId,
                Payload = payload
            }, ct);
            if (!sent)
            {
                _logger.LogWarning("{Request} not sent: not connected to the Agent Service", label);
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            try
            {
                var reply = await tcs.Task.ConfigureAwait(false);
                return reply.Payload?.Deserialize<TReply>();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{Request} timed out waiting for result", label);
                return null;
            }
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    public async Task SubmitCollectionRecordsAsync(
        IReadOnlyList<CollectionRecord> records,
        CancellationToken ct)
    {
        if (records.Count == 0 || _writer is null)
            return;

        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.CollectionRecordSubmit,
            Payload = JsonSerializer.SerializeToElement(
                new CollectionRecordSubmitPayload { Records = records })
        };
        await WriteEnvelopeAsync(envelope, ct);
    }

    public Task<bool> SubmitInactivityAttemptAsync(
        InactivityCaptureAttemptPayload attempt,
        ReadOnlyMemory<byte> jpegBytes,
        CancellationToken ct) =>
        _evidenceTransferClient.SubmitAsync(attempt, jpegBytes, ct);

    public async Task<bool> SubmitPeriodicScreenshotAsync(
        DateTimeOffset capturedAt,
        ReadOnlyMemory<byte> jpegBytes,
        CancellationToken ct)
    {
        if (jpegBytes.IsEmpty || _writer is null)
            return false;

        var captureId = Guid.NewGuid();
        var chunkCount = (int)Math.Ceiling(jpegBytes.Length / (double)Constants.EvidenceChunkSizeBytes);
        using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ackTask = WaitForEvidenceAckAsync(captureId, ackCts.Token);

        try
        {
            if (!await WriteEnvelopeAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.PeriodicScreenshotStart,
                CorrelationId = captureId.ToString("N"),
                Payload = JsonSerializer.SerializeToElement(
                    new PeriodicScreenshotStartPayload(captureId, capturedAt, jpegBytes.Length, chunkCount))
            }, ct).ConfigureAwait(false))
                return false;

            var index = 0;
            var offset = 0;
            while (offset < jpegBytes.Length)
            {
                var length = Math.Min(Constants.EvidenceChunkSizeBytes, jpegBytes.Length - offset);
                var slice = jpegBytes.Slice(offset, length);
                if (!await WriteEnvelopeAsync(new IpcEnvelope
                {
                    Type = IpcMessageTypes.PeriodicScreenshotChunk,
                    CorrelationId = captureId.ToString("N"),
                    Payload = JsonSerializer.SerializeToElement(
                        new PeriodicScreenshotChunkPayload(captureId, index, Convert.ToBase64String(slice.Span)))
                }, ct).ConfigureAwait(false))
                    return false;
                offset += length;
                index++;
            }

            if (!await WriteEnvelopeAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.PeriodicScreenshotComplete,
                CorrelationId = captureId.ToString("N"),
                Payload = JsonSerializer.SerializeToElement(new PeriodicScreenshotCompletePayload(captureId))
            }, ct).ConfigureAwait(false))
                return false;

            var ack = await ackTask.ConfigureAwait(false);
            return ack?.Accepted == true;
        }
        finally
        {
            // A failed or cancelled send means no ack is coming: stop waiting for it.
            await ackCts.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task<EvidenceTransferAckPayload?> WaitForEvidenceAckAsync(Guid attemptId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<EvidenceTransferAckPayload?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEvidenceAcks[attemptId] = tcs;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await using var reg = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the sender (a send failed) is not a timeout — don't log it as one.
                if (!ct.IsCancellationRequested)
                    _logger.LogWarning("Evidence transfer ack timed out for attempt {AttemptId}", attemptId);
                return null;
            }
        }
        finally
        {
            _pendingEvidenceAcks.TryRemove(attemptId, out _);
        }
    }

    /// <summary>
    /// Writes one envelope. Returns false without throwing when there is no connection or the pipe
    /// broke mid-write, so callers can fail fast instead of waiting for a reply that can't come.
    /// </summary>
    private async Task<bool> WriteEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var writer = _writer;
            if (writer is null)
                return false;
            await writer.WriteLineAsync(JsonSerializer.Serialize(envelope).AsMemory(), ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The read loop sees the same drop, resets the connection and reconnects.
            _logger.LogWarning("IPC write of {Type} failed: {Msg}", envelope.Type, ex.Message);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, Encoding utf8, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (line.Length > Constants.MaxMessageLengthBytes) continue;

                IpcEnvelope? envelope;
                try { envelope = JsonSerializer.Deserialize<IpcEnvelope>(line); }
                catch (JsonException) { continue; }

                if (envelope is null)
                    continue;

                // Complete any pending request/response pair first. Match on correlation
                // id only — new reply types (e.g. FacePhotoValidateResult) must not sit
                // in _pending until the 25s timeout just because this list was not updated.
                if (!string.IsNullOrEmpty(envelope.CorrelationId)
                    && _pending.TryGetValue(envelope.CorrelationId, out var pending))
                {
                    pending.TrySetResult(envelope);
                }

                switch (envelope.Type)
                {
                    case IpcMessageTypes.StatusResponse:
                    {
                        var status = envelope.Payload?.Deserialize<StatusResponsePayload>();
                        if (status is not null)
                        {
                            LastKnownStatus = status;
                            OnStatusReceived?.Invoke(status);
                            OnStateReceived?.Invoke(status.State);
                        }
                        break;
                    }
                    case IpcMessageTypes.LifecycleResult:
                    {
                        // Fire OnStatusReceived before OnStateReceived — same order as the
                        // StatusResponse case below. Listeners (e.g. App.xaml.cs's navigation
                        // router) that derive routing flags from OnStatusReceived must see
                        // that update before OnStateReceived triggers the actual navigation,
                        // or they'll route on stale state.
                        var result = envelope.Payload?.Deserialize<LifecycleResultPayload>();
                        if (result is not null)
                        {
                            if (result.Session is not null)
                            {
                                var synth = new StatusResponsePayload(
                                    result.State, DateTimeOffset.UtcNow, result.Session);
                                LastKnownStatus = synth;
                                OnStatusReceived?.Invoke(synth);
                            }
                            OnStateReceived?.Invoke(result.State);
                        }
                        break;
                    }
                    case IpcMessageTypes.PolicyPush:
                    {
                        var policyPayload = envelope.Payload?.Deserialize<PolicyPushPayload>();
                        if (policyPayload?.Policy is not null)
                        {
                            LastKnownPolicy = policyPayload.Policy;
                            OnPolicyReceived?.Invoke(policyPayload.Policy);
                        }
                        break;
                    }
                    case IpcMessageTypes.NotificationPush:
                    {
                        var notificationPayload = envelope.Payload?.Deserialize<NotificationPushPayload>();
                        if (notificationPayload is not null)
                            OnNotificationReceived?.Invoke(notificationPayload);
                        break;
                    }
                    case IpcMessageTypes.DevicePairingResult:
                    {
                        var pairingResult = envelope.Payload?.Deserialize<DevicePairingResultPayload>();
                        if (pairingResult is not null)
                            OnDevicePairingResult?.Invoke(pairingResult);
                        break;
                    }
                    case IpcMessageTypes.CollectionRecordAck:
                    {
                        // AGT-02: the Service could not store what we sent (queue full / disk error).
                        var ack = envelope.Payload?.Deserialize<CollectionRecordAckPayload>();
                        if (ack?.ErrorCode is { } ackError)
                            _logger.LogWarning(
                                "Agent Service did not store all collected records ({ErrorCode}); accepted {Accepted}",
                                ackError, ack.AcceptedCount);
                        break;
                    }
                    case IpcMessageTypes.EvidenceTransferAck:
                    {
                        var ack = envelope.Payload?.Deserialize<EvidenceTransferAckPayload>();
                        if (ack is not null && _pendingEvidenceAcks.TryGetValue(ack.AttemptId, out var evidenceTcs))
                            evidenceTcs.TrySetResult(ack);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("IPC read loop ended: {Msg}", ex.Message);
        }
        finally
        {
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();
            foreach (var kv in _pendingEvidenceAcks)
                kv.Value.TrySetResult(null);
            _pendingEvidenceAcks.Clear();
            // Reset before signalling: OnDisconnected handlers (collector stop/drain) may write, and
            // those writes must fail fast rather than hit the dead pipe.
            await ResetConnectionAsync();
            OnDisconnected?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();

            // Let the connect loop see the cancellation and exit before the connection is torn down.
            var loopExited = true;
            if (_connectLoop is not null)
            {
                try
                {
                    await _connectLoop.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException)
                {
                    loopExited = false;
                    _logger.LogWarning("IPC connect loop did not stop within 2s of dispose");
                }
            }
            if (loopExited)
                _cts.Dispose();
        }
        await ResetConnectionAsync();
        // _writeLock is deliberately not disposed: a request still finishing during shutdown would
        // otherwise get ObjectDisposedException from WaitAsync/Release.
    }
}

/// <summary>
/// Builds and sends the start/chunk/complete envelope sequence for one inactivity evidence
/// transfer (Task 1 contracts: <see cref="EvidenceTransferStartPayload"/>,
/// <see cref="EvidenceTransferChunkPayload"/>, <see cref="EvidenceTransferCompletePayload"/>), then
/// waits for the correlated <see cref="EvidenceTransferAckPayload"/>. Splits raw bytes into
/// <see cref="Constants.EvidenceChunkSizeBytes"/>-sized chunks; a metadata-only attempt (empty
/// <c>jpegBytes</c>) sends only start + complete, no chunk envelopes.
/// </summary>
/// <remarks>
/// The actual envelope write (<paramref name="send"/>, false when not connected) and ack wait
/// (<paramref name="waitForAck"/>) are supplied by the caller (<see cref="NamedPipeClient"/>) so this
/// class is unit-testable without a live Named Pipe — see <c>EvidenceTransferClientTests</c>.
/// </remarks>
public sealed class EvidenceTransferClient(
    Func<IpcEnvelope, CancellationToken, Task<bool>> send,
    Func<Guid, CancellationToken, Task<EvidenceTransferAckPayload?>> waitForAck)
{
    public async Task<bool> SubmitAsync(
        InactivityCaptureAttemptPayload attempt,
        ReadOnlyMemory<byte> jpegBytes,
        CancellationToken ct)
    {
        var correlationId = attempt.AttemptId.ToString("N");
        var chunkCount = jpegBytes.IsEmpty
            ? 0
            : (int)Math.Ceiling(jpegBytes.Length / (double)Constants.EvidenceChunkSizeBytes);

        // Register for the ack before the first send: a fast Service can ack before the Complete
        // write returns, and an ack nobody is waiting for yet is dropped by the read loop.
        using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ackTask = waitForAck(attempt.AttemptId, ackCts.Token);

        try
        {
            if (!await send(new IpcEnvelope
            {
                Type = IpcMessageTypes.EvidenceTransferStart,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new EvidenceTransferStartPayload(attempt, jpegBytes.Length, chunkCount))
            }, ct).ConfigureAwait(false))
                return false;

            var index = 0;
            var offset = 0;
            while (offset < jpegBytes.Length)
            {
                var length = Math.Min(Constants.EvidenceChunkSizeBytes, jpegBytes.Length - offset);
                var slice = jpegBytes.Slice(offset, length);
                if (!await send(new IpcEnvelope
                {
                    Type = IpcMessageTypes.EvidenceTransferChunk,
                    CorrelationId = correlationId,
                    Payload = JsonSerializer.SerializeToElement(
                        new EvidenceTransferChunkPayload(attempt.AttemptId, index, Convert.ToBase64String(slice.Span)))
                }, ct).ConfigureAwait(false))
                    return false;
                offset += length;
                index++;
            }

            if (!await send(new IpcEnvelope
            {
                Type = IpcMessageTypes.EvidenceTransferComplete,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new EvidenceTransferCompletePayload(attempt.AttemptId))
            }, ct).ConfigureAwait(false))
                return false;

            var ack = await ackTask.ConfigureAwait(false);
            return ack?.Accepted ?? false;
        }
        finally
        {
            // A failed or cancelled send means no ack is coming: stop waiting for it.
            await ackCts.CancelAsync().ConfigureAwait(false);
        }
    }
}
