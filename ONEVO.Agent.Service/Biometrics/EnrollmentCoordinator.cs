namespace ONEVO.Agent.Service.Biometrics;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;

public sealed record EnrollmentSessionResult(
    bool Success, string? ErrorCode, Guid AttemptId, string? AwsSessionId, string? Region,
    string? ChallengeType, string? AccessKeyId, string? SecretAccessKey, string? SessionToken,
    DateTimeOffset? CredentialsExpireAt);

public sealed record EnrollmentCompletionResult(bool Success, string? ErrorCode, string? ProfileStatus);

/// <summary>
/// Orchestrates the enrollment subset of the biometric flow: Service holds the Device JWT
/// (never handed to the Tray, per §8.2 of the architecture doc) and makes the two backend
/// calls on the Tray's behalf. The Tray only ever sees the short-lived AWS capture credentials
/// this returns — never the Device JWT itself.
/// </summary>
public sealed class EnrollmentCoordinator
{
    private readonly ILogger<EnrollmentCoordinator> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;

    public EnrollmentCoordinator(
        ILogger<EnrollmentCoordinator> logger, OnevoApiClient apiClient, CredentialStore credentials)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
    }

    public async Task<EnrollmentSessionResult> StartAsync(CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return new EnrollmentSessionResult(false, "NO_DEVICE_CREDENTIAL",
                Guid.Empty, null, null, null, null, null, null, null);
        }

        var result = await _apiClient.CreateEnrollmentAttemptAsync(jwt, ct);
        if (!result.Success || result.Attempt is null)
        {
            _logger.LogWarning("CreateEnrollmentAttempt failed: {ErrorCode}", result.ErrorCode);
            return new EnrollmentSessionResult(false, result.ErrorCode ?? "SERVICE_UNAVAILABLE",
                Guid.Empty, null, null, null, null, null, null, null);
        }

        var attempt = result.Attempt;
        return new EnrollmentSessionResult(
            true, null, attempt.AttemptId, attempt.AwsSessionId, attempt.Region, attempt.ChallengeType,
            attempt.AccessKeyId, attempt.SecretAccessKey, attempt.SessionToken, attempt.CredentialsExpireAt);
    }

    public async Task<EnrollmentCompletionResult> CompleteAsync(Guid attemptId, CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
            return new EnrollmentCompletionResult(false, "NO_DEVICE_CREDENTIAL", null);

        var result = await _apiClient.CompleteEnrollmentAttemptAsync(jwt, attemptId, ct);
        if (!result.Success)
            _logger.LogWarning("CompleteEnrollmentAttempt failed: {ErrorCode}", result.ErrorCode);

        return new EnrollmentCompletionResult(result.Success, result.ErrorCode, result.ProfileStatus);
    }
}
