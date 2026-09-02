namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public interface INamedPipeClient
{
    event Action? OnDisconnected;
    event Action<MonitoringState>? OnStateReceived;
    event Action<StatusResponsePayload>? OnStatusReceived;
    event Action<AgentPolicy>? OnPolicyReceived;
    event Action<NotificationPushPayload>? OnNotificationReceived;
    event Action<DevicePairingResultPayload>? OnDevicePairingResult;

    /// <summary>Last status received from the service — null until first response arrives.</summary>
    StatusResponsePayload? LastKnownStatus { get; }

    /// <summary>Last policy pushed from the service — null until first PolicyPush arrives.</summary>
    AgentPolicy? LastKnownPolicy { get; }

    Task StartAsync(CancellationToken ct);
    Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct);
    Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Sends a lifecycle command and waits for the correlated LifecycleResult (or timeout).
    /// </summary>
    Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null);

    /// <summary>
    /// Submits activation code and waits for EnrollmentResult (or timeout).
    /// </summary>
    Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct);

    /// <summary>
    /// Starts a browser-based device pairing and waits for the correlated
    /// DevicePairingStarted reply (or timeout) carrying the browser URL to open. The
    /// terminal outcome arrives later, asynchronously, via OnDevicePairingResult.
    /// </summary>
    Task<DevicePairingStartedPayload?> SendDevicePairingStartAsync(
        string deviceName, string deviceOs, string clientVersion, CancellationToken ct);

    /// <summary>Cancels an in-progress device pairing poll loop. Fire-and-forget — no reply expected.</summary>
    Task SendDevicePairingCancelAsync(CancellationToken ct);

    /// <summary>
    /// Requests sign-out and waits for LogoutResult (or timeout).
    /// </summary>
    Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct);

    /// <summary>
    /// Submits one inactivity capture attempt's metadata (and, for a <c>captured</c> outcome, its
    /// JPEG bytes) via Task 1's start/chunk/complete evidence-transfer envelopes, and waits for the
    /// correlated <see cref="EvidenceTransferAckPayload"/> (bounded). Metadata-only outcomes pass an
    /// empty <paramref name="jpegBytes"/> and send zero chunk envelopes. Returns whether the Service
    /// accepted the transfer. A non-overriding implementer returns <c>false</c> by default — the
    /// Service-side receiver lands in a later task, so today only <see cref="NamedPipeClient"/>
    /// implements this for real.
    /// </summary>
    Task<bool> SubmitInactivityAttemptAsync(
        InactivityCaptureAttemptPayload attempt,
        ReadOnlyMemory<byte> jpegBytes,
        CancellationToken ct) => Task.FromResult(false);

    /// <summary>Requests a new enrollment liveness session and waits for BiometricEnrollmentSessionReady (or timeout).</summary>
    Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct);

    /// <summary>Reports the WebView2 capture outcome and waits for the final BiometricEnrollmentResult (or timeout).</summary>
    Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct);
}
