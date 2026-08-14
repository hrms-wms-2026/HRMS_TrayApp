namespace ONEVO.Agent.Shared.Models;

/// <summary>Outcome codes for an inactivity capture attempt.</summary>
public static class InactivityCaptureOutcomes
{
    public const string Captured = "captured";
    public const string Declined = "declined";
    public const string TimedOut = "timed_out";
    public const string ActivityResumed = "activity_resumed";
    public const string MonitoringStopped = "monitoring_stopped";
    public const string CaptureFailed = "capture_failed";
}

/// <summary>
/// One inactivity-prompt/capture attempt lifecycle record.
/// Privacy: never carries tenant/employee identity or image bytes — identity is inferred
/// server-side from the authenticated device/session, and image bytes travel separately
/// via the evidence-transfer chunk messages.
/// </summary>
public sealed record InactivityCaptureAttemptPayload
{
    public required Guid AttemptId { get; init; }
    public required string PolicyVersion { get; init; }
    public required DateTimeOffset IdleStartedAt { get; init; }
    public required DateTimeOffset PromptedAt { get; init; }
    public DateTimeOffset? DecisionAt { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public required int IdleDurationSeconds { get; init; }
    public required int MonitorCount { get; init; }
    public required string Outcome { get; init; }
    public string? FailureCode { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
}
