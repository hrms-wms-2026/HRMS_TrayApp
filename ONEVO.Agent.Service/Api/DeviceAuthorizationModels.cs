namespace ONEVO.Agent.Service.Api;

using System.Text.Json.Serialization;

public enum DeviceAuthorizationPollState
{
    AuthorizationPending,
    SlowDown,
    ExpiredToken,
    AccessDenied,
    Authorized,
    ServiceUnavailable,
}

public sealed record DeviceAuthorizationStartResult(
    bool Success,
    string? ErrorCode,
    string? DeviceCode,
    string? UserCode,
    string? VerificationUri,
    string? VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

public sealed record DeviceAuthorizationPollResult(
    DeviceAuthorizationPollState State,
    TrayAuthPayload? Auth);

public sealed record DeviceAuthorizationStartPayload(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("verification_uri_complete")] string VerificationUriComplete,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("interval_seconds")] int IntervalSeconds);
