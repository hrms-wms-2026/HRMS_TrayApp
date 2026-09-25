namespace ONEVO.Agent.Shared.IPC;

using ONEVO.Agent.Shared.Models;

public static class IpcMessageTypes
{
    public const string StatusRequest  = "StatusRequest";
    public const string StatusResponse = "StatusResponse";
    public const string NonceChallenge = "NonceChallenge";
    public const string NonceResponse  = "NonceResponse";

    /// <summary>Tray → Service: one or more privacy-scrubbed collection records.</summary>
    public const string CollectionRecordSubmit = "CollectionRecordSubmit";

    /// <summary>Service → Tray: acknowledgement for a submitted batch.</summary>
    public const string CollectionRecordAck = "CollectionRecordAck";

    /// <summary>Service → Tray: effective policy for collector enablement.</summary>
    public const string PolicyPush = "PolicyPush";

    /// <summary>Service → Tray: a wellness notification (break reminder / long idle) to show as a toast.</summary>
    public const string NotificationPush = "NotificationPush";

    /// <summary>Tray → Service: employee-entered activation code from web portal.</summary>
    public const string ActivationCodeSubmit = "ActivationCodeSubmit";

    /// <summary>Service → Tray: result of enrollment attempt.</summary>
    public const string EnrollmentResult = "EnrollmentResult";

    /// <summary>Tray → Service: employee lifecycle action (clock-in, break, clock-out).</summary>
    public const string LifecycleCommand = "LifecycleCommand";

    /// <summary>Service → Tray: result of a lifecycle action.</summary>
    public const string LifecycleResult = "LifecycleResult";

    /// <summary>Tray → Service: employee requested sign-out.</summary>
    public const string LogoutRequest = "LogoutRequest";

    /// <summary>Service → Tray: result of a sign-out attempt.</summary>
    public const string LogoutResult = "LogoutResult";

    /// <summary>Tray → Service: ask the backend whether a newer installer exists.</summary>
    public const string UpdateCheckRequest = "UpdateCheckRequest";

    /// <summary>Service → Tray: result of an update check.</summary>
    public const string UpdateCheckResult = "UpdateCheckResult";

    /// <summary>Tray → Service: begin an evidence transfer for one inactivity capture attempt.</summary>
    public const string EvidenceTransferStart = "EvidenceTransferStart";

    /// <summary>Tray → Service: one base64-encoded chunk of the evidence image.</summary>
    public const string EvidenceTransferChunk = "EvidenceTransferChunk";

    /// <summary>Tray → Service: all chunks for the attempt have been sent.</summary>
    public const string EvidenceTransferComplete = "EvidenceTransferComplete";

    /// <summary>Service → Tray: acknowledgement for a completed (or rejected) evidence transfer.</summary>
    public const string EvidenceTransferAck = "EvidenceTransferAck";

    /// <summary>Tray → Service: begin a working-hours screenshot upload.</summary>
    public const string PeriodicScreenshotStart = "PeriodicScreenshotStart";

    /// <summary>Tray → Service: one chunk of a working-hours screenshot.</summary>
    public const string PeriodicScreenshotChunk = "PeriodicScreenshotChunk";

    /// <summary>Tray → Service: all chunks of a working-hours screenshot have been sent.</summary>
    public const string PeriodicScreenshotComplete = "PeriodicScreenshotComplete";

    /// <summary>Tray → Service: employee wants to start biometric enrollment.</summary>
    public const string BiometricEnrollmentStart = "BiometricEnrollmentStart";

    /// <summary>Service → Tray: AWS session + short-lived scoped credentials for the WebView2 capture client.</summary>
    public const string BiometricEnrollmentSessionReady = "BiometricEnrollmentSessionReady";

    /// <summary>Tray → Service: the WebView2 capture finished (or failed) — ask the Service to ask the backend to complete the attempt.</summary>
    public const string BiometricEnrollmentCaptureFinished = "BiometricEnrollmentCaptureFinished";

    /// <summary>Service → Tray: final enrollment outcome after the backend's CompleteEnrollmentAttempt call.</summary>
    public const string BiometricEnrollmentResult = "BiometricEnrollmentResult";

    /// <summary>Tray → Service: start a browser-based device pairing (RFC 8628 device authorization grant).</summary>
    public const string DevicePairingStart = "DevicePairingStart";

    /// <summary>Service → Tray: correlated reply to DevicePairingStart with the browser URL to open.</summary>
    public const string DevicePairingStarted = "DevicePairingStarted";

    /// <summary>Tray → Service: cancel an in-progress device pairing poll loop.</summary>
    public const string DevicePairingCancel = "DevicePairingCancel";

    /// <summary>Service → Tray: unsolicited push with the terminal outcome of a device pairing (approved, denied, or expired).</summary>
    public const string DevicePairingResult = "DevicePairingResult";

    /// <summary>Tray → Service: employee submits a "Request location change" action (remote work mode only).</summary>
    public const string LocationChangeSubmit = "LocationChangeSubmit";

    /// <summary>Service → Tray: result of a LocationChangeSubmit call.</summary>
    public const string LocationChangeSubmitResult = "LocationChangeSubmitResult";

    /// <summary>Tray → Service: is there an approved-but-not-yet-applied location change request right
    /// now? Polled from the tray's main clocked-in screen after every clock-in (§ design note on
    /// GetPendingLocationChangeDecisionQuery: not every clock-in path submits a check-in, so this
    /// can't be tied to any one specific clock-in reply).</summary>
    public const string LocationChangePendingCheck = "LocationChangePendingCheck";

    /// <summary>Service → Tray: reply to LocationChangePendingCheck.</summary>
    public const string LocationChangePendingResult = "LocationChangePendingResult";

    /// <summary>Tray → Service: employee answered the "save this as your new location?" prompt.</summary>
    public const string LocationChangeRespond = "LocationChangeRespond";

    /// <summary>Service → Tray: result of a LocationChangeRespond call.</summary>
    public const string LocationChangeRespondResult = "LocationChangeRespondResult";

    /// <summary>Tray → Service: employee confirmed today's work location on the daily screen.</summary>
    public const string WorkLocationConfirm = "WorkLocationConfirm";

    /// <summary>Service → Tray: result of a WorkLocationConfirm call.</summary>
    public const string WorkLocationConfirmResult = "WorkLocationConfirmResult";

    /// <summary>Tray → Service: preview a clock-in/out selfie against AWS before lifecycle.</summary>
    public const string FacePhotoValidate = "FacePhotoValidate";

    /// <summary>Service → Tray: DetectFaces + CompareFaces result used to allow clock-in or force a retake.</summary>
    public const string FacePhotoValidateResult = "FacePhotoValidateResult";

    /// <summary>Tray → Service: employee accepted the pending legal documents shown on the consent screen.</summary>
    public const string LegalAcceptanceSubmit = "LegalAcceptanceSubmit";

    /// <summary>Service → Tray: result of a LegalAcceptanceSubmit call.</summary>
    public const string LegalAcceptanceResult = "LegalAcceptanceResult";
}

public enum LifecycleAction
{
    ClockIn,
    StartBreak,
    EndBreak,
    ClockOut
}

public sealed record NonceChallengePayload(string Nonce);
public sealed record NonceResponsePayload(string Nonce);

/// <summary>Authoritative presence-session snapshot owned by the Service.</summary>
public sealed record SessionSnapshot(
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    bool IsOnBreak,
    DateTimeOffset? CurrentBreakStartedAt,
    TimeSpan AccumulatedBreak,
    TimeSpan AccumulatedWork,
    string? ScheduleDisplay,
    int BreakSessionCount,
    TimeSpan AccumulatedIdle = default,
    bool IsIdle = false,
    DateTimeOffset? CurrentIdleStartedAt = null,
    int? BreakAllowanceMinutes = null,
    int CompletedBreakMinutes = 0,
    bool CanStartBreak = true);

public sealed record StatusResponsePayload(
    MonitoringState State,
    DateTimeOffset Timestamp,
    SessionSnapshot? Session = null);

public sealed record LifecycleCommandPayload(
    LifecycleAction Action,
    string? BreakReason = null);

public sealed record LifecycleResultPayload(
    bool Success,
    string? ErrorCode,
    string? Message,
    MonitoringState State,
    SessionSnapshot? Session);

public sealed record CollectionRecordSubmitPayload
{
    public required IReadOnlyList<CollectionRecord> Records { get; init; }
}

public sealed record CollectionRecordAckPayload
{
    public required int AcceptedCount { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record PolicyPushPayload
{
    public required AgentPolicy Policy { get; init; }
}

public sealed record NotificationPushPayload
{
    public required Guid NotificationId { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
}

public sealed record ActivationCodeSubmitPayload(string Code);

/// <summary>Wire-format mirror of the backend's PendingLegalDocumentDto — just enough for the
/// tray consent screen to list a document and let the employee open it.</summary>
public sealed record PendingLegalDocumentPayload(
    string DocumentType,
    string Version,
    string Title,
    string? ContentUrl,
    string ContentEndpoint);

public sealed record EnrollmentResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }   // "INVALID_CODE" | "EXPIRED" | "ALREADY_ENROLLED" | "SERVICE_UNAVAILABLE"
    public string? EmployeeName { get; init; }   // set on success for greeting
    public string? EmployeeEmail { get; init; }  // set on success for the workspace-setup screen
    public string? EmployeeNumber { get; init; } // set on success for the workspace-setup screen
    public string? EmployeeProfileStatus { get; init; }
    public string? DepartmentName { get; init; }
    public string? WorkModeLabel { get; init; }
    public string? OfficeName { get; init; }
    public string? OrganizationName { get; init; }
    public bool RequiresLegalAcceptance { get; init; }
    public IReadOnlyList<PendingLegalDocumentPayload>? PendingLegalDocuments { get; init; }
}

public sealed record LogoutResultPayload(bool Success, string? ErrorCode);

public sealed record UpdateCheckRequestPayload(string CurrentVersion);

public sealed record UpdateCheckResultPayload(
    bool Success,
    bool UpdateAvailable,
    bool Mandatory,
    string? LatestVersion,
    string? DownloadUrl,
    string? Sha256,
    long FileSizeBytes,
    string? ReleaseNotes,
    string? ErrorCode);

public sealed record BiometricEnrollmentStartPayload;

public sealed record BiometricEnrollmentSessionReadyPayload(
    bool Success,
    string? ErrorCode,
    Guid AttemptId,
    string? AwsSessionId,
    string? Region,
    string? ChallengeType,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? SessionToken,
    DateTimeOffset? CredentialsExpireAt);

/// <summary>CaptureSucceeded distinguishes a clean AWS-side liveness completion from a local
/// capture-side failure (camera denied/occupied, WebView2 crash, cancellation) — the Service
/// still asks the backend to check the AWS session either way, since AWS is the source of truth.</summary>
public sealed record BiometricEnrollmentCaptureFinishedPayload(Guid AttemptId, bool CaptureSucceeded, string? ClientErrorCode);

public sealed record BiometricEnrollmentResultPayload(bool Success, string? ErrorCode, string? ProfileStatus);

public sealed record DevicePairingStartPayload(string DeviceName, string DeviceOs, string ClientVersion);

public sealed record DevicePairingStartedPayload(
    bool Success,
    string? ErrorCode,
    string? VerificationUri = null,
    string? VerificationUriComplete = null,
    int ExpiresInSeconds = 0,
    int IntervalSeconds = 0);

/// <summary>Unsolicited push (not a correlated reply) — same shape as EnrollmentResultPayload
/// so the ViewModel drives one shared success/failure path for both connect flows.</summary>
public sealed record DevicePairingResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }   // "ACCESS_DENIED" | "EXPIRED" | "SERVICE_UNAVAILABLE" | "INVALID_STATE"
    public string? EmployeeName { get; init; }
    public string? EmployeeEmail { get; init; }
    public string? EmployeeNumber { get; init; }
    public string? EmployeeProfileStatus { get; init; }
    public string? DepartmentName { get; init; }
    public string? WorkModeLabel { get; init; }
    public string? OfficeName { get; init; }
    public string? OrganizationName { get; init; }
    public bool RequiresLegalAcceptance { get; init; }
    public IReadOnlyList<PendingLegalDocumentPayload>? PendingLegalDocuments { get; init; }
}

/// <summary>Minimal shape the tray UI needs for a location change request — just enough to drive
/// the "Request location change" confirmation and the post-clock-in re-prompt. The backend's
/// richer LocationChangeRequestResponse (reviewer, coordinates, etc.) is not forwarded over IPC;
/// only OnevoApiClient's wire-format mirror sees that.</summary>
public sealed record LocationChangeRequestSummaryPayload(
    Guid Id,
    string Status, // LocationChangeRequest.StatusPending | StatusApproved | StatusRejected | StatusCancelled | StatusApplied
    DateTimeOffset RequestedAt);

public sealed record LocationChangeSubmitPayload(
    double Latitude, double Longitude, double? AccuracyMeters, string Reason);

public sealed record LocationChangeSubmitResultPayload(
    bool Success, string? ErrorCode, LocationChangeRequestSummaryPayload? Request);

public sealed record LocationChangePendingCheckPayload;

public sealed record LocationChangePendingResultPayload(
    bool Success, string? ErrorCode, LocationChangeRequestSummaryPayload? Request);

public sealed record LocationChangeRespondPayload(Guid Id, bool Apply);

public sealed record LocationChangeRespondResultPayload(
    bool Success, string? ErrorCode, LocationChangeRequestSummaryPayload? Request);

public sealed record WorkLocationConfirmPayload(
    string LocationType, double? Latitude, double? Longitude, double? AccuracyMeters);

public sealed record WorkLocationConfirmResultPayload(bool Success, string? ErrorCode);

/// <param name="Purpose">"enrollment", "clock_in" or "clock_out" — see <see cref="FacePhotoValidatePurposes"/>.
/// Only enrollment lets the backend save a first reference face.</param>
public sealed record FacePhotoValidatePayload(string Format, string Data, string? Purpose = null);

public static class FacePhotoValidatePurposes
{
    public const string Enrollment = "enrollment";
    public const string ClockIn = "clock_in";
    public const string ClockOut = "clock_out";
}

public sealed record FacePhotoValidateResultPayload(
    bool Success,
    string? ErrorCode,
    bool LightingOk,
    bool FaceVisible,
    bool NoSunglassesOrMask,
    bool IsMatch,
    bool CanProceed,
    float? Similarity,
    string? FailureReason);

public sealed record LegalAcceptanceItemPayload(string DocumentType, string Version);

/// <summary>Tray → Service: the employee accepted every document shown on the consent screen in
/// one action (the backend requires the full pending set in a single call, not one at a time).</summary>
public sealed record LegalAcceptanceSubmitPayload(IReadOnlyList<LegalAcceptanceItemPayload> Acceptances);

public sealed record LegalAcceptanceResultPayload(bool Success, string? ErrorCode);
