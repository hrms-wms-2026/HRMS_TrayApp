namespace ONEVO.Agent.Service.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Typed wrapper over the "OnevoApi" named HttpClient for the TrayActivation
/// device-code endpoints — exchange (enroll/complete), refresh (login), revoke (logout). §9/§10.
/// </summary>
public sealed class OnevoApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OnevoApiClient> _logger;

    public OnevoApiClient(IHttpClientFactory httpClientFactory, ILogger<OnevoApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Exchange a one-time activation code for a device credential (enroll/complete).</summary>
    public Task<TrayAuthResult> ExchangeActivationCodeAsync(
        string code, string deviceName, string deviceOs, string deviceFingerprint, CancellationToken ct) =>
        PostAuthAsync(
            AgentApiRoutes.ActivationExchange,
            new ExchangeRequestBody(code, deviceName, deviceOs, deviceFingerprint),
            ct);

    /// <summary>Resume/refresh the device session using a stored refresh token (login).</summary>
    public Task<TrayAuthResult> RefreshTokenAsync(
        string refreshToken, string deviceFingerprint, CancellationToken ct) =>
        PostAuthAsync(
            AgentApiRoutes.ActivationRefresh,
            new RefreshRequestBody(refreshToken, deviceFingerprint),
            ct);

    /// <summary>Revoke the current device session (logout). Best-effort — caller clears local state either way.</summary>
    public async Task<bool> RevokeDeviceAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.ActivationRevoke);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Device revoke call failed — treating as best-effort no-op");
            return false;
        }
    }

    /// <summary>Fetch the effective monitoring policy for this device (§Task3). Auth: Bearer Device JWT.</summary>
    public async Task<PolicyResult> GetEffectivePolicyAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentApiRoutes.TrayPolicy);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.TrayPolicy);
            return new PolicyResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
            return new PolicyResult(false, "UNAUTHORIZED", null);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OnevoApi call to {Route} returned {Status}", AgentApiRoutes.TrayPolicy, (int)response.StatusCode);
            return new PolicyResult(false, "SERVICE_UNAVAILABLE", null);
        }

        TrayAgentPolicyPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<TrayAgentPolicyPayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", AgentApiRoutes.TrayPolicy);
            return new PolicyResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Version))
        {
            _logger.LogWarning("OnevoApi response from {Route} was empty or missing a version", AgentApiRoutes.TrayPolicy);
            return new PolicyResult(false, "SERVICE_UNAVAILABLE", null);
        }

        var policy = new AgentPolicy
        {
            Version = payload.Version,
            ActivitySignalEnabled = payload.ActivitySignalEnabled,
            AppUsageEnabled = payload.AppUsageEnabled,
            ScreenshotEnabled = payload.ScreenshotEnabled,
            InactivityScreenshotEnabled = payload.InactivityScreenshotEnabled,
            CameraVerificationEnabled = payload.CameraVerificationEnabled,
            ValidUntil = payload.ValidUntil
        };

        return new PolicyResult(true, null, policy);
    }

    /// <summary>Polls for pending break/idle notifications. Auth: Bearer Device JWT.</summary>
    public async Task<List<PendingNotificationPayload>> GetPendingNotificationsAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, AgentApiRoutes.PendingTrayNotifications);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            var body = await response.Content.ReadFromJsonAsync<PendingNotificationsResponse>(cancellationToken: ct);
            return body?.Notifications ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.PendingTrayNotifications);
            return [];
        }
    }

    /// <summary>Acknowledges a delivered notification so it isn't re-sent next poll. Best-effort.</summary>
    public async Task AckNotificationAsync(string accessToken, Guid notificationId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        var route = string.Format(AgentApiRoutes.AckTrayNotification, notificationId);
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", route);
        }
    }

    /// <summary>Creates a new enrollment attempt. Auth: Bearer Device JWT.</summary>
    public async Task<EnrollmentAttemptResult> CreateEnrollmentAttemptAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.BiometricEnrollmentAttemptCreate);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.BiometricEnrollmentAttemptCreate);
            return new EnrollmentAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new EnrollmentAttemptResult(
                false,
                response.StatusCode == HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "SERVICE_UNAVAILABLE",
                null);
        }

        EnrollmentAttemptPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<EnrollmentAttemptPayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", AgentApiRoutes.BiometricEnrollmentAttemptCreate);
            return new EnrollmentAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        return payload is null
            ? new EnrollmentAttemptResult(false, "SERVICE_UNAVAILABLE", null)
            : new EnrollmentAttemptResult(true, null, payload);
    }

    /// <summary>Completes an enrollment attempt. Auth: Bearer Device JWT.</summary>
    public async Task<CompleteEnrollmentResult> CompleteEnrollmentAttemptAsync(
        string accessToken, Guid attemptId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        var route = string.Format(AgentApiRoutes.BiometricEnrollmentAttemptComplete, attemptId);
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", route);
            return new CompleteEnrollmentResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new CompleteEnrollmentResult(
                false,
                response.StatusCode == HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "SERVICE_UNAVAILABLE",
                null);
        }

        BiometricProfilePayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<BiometricProfilePayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", route);
            return new CompleteEnrollmentResult(false, "SERVICE_UNAVAILABLE", null);
        }

        return payload is null
            ? new CompleteEnrollmentResult(false, "SERVICE_UNAVAILABLE", null)
            : new CompleteEnrollmentResult(true, null, payload.Status);
    }

    private async Task<TrayAuthResult> PostAuthAsync(string route, object body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body)
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", route);
            return new TrayAuthResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized)
            return new TrayAuthResult(false, "UNAUTHORIZED", null);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OnevoApi call to {Route} returned {Status}", route, (int)response.StatusCode);
            return new TrayAuthResult(false, "SERVICE_UNAVAILABLE", null);
        }

        TrayAuthPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<TrayAuthPayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", route);
            return new TrayAuthResult(false, "SERVICE_UNAVAILABLE", null);
        }

        return payload is null
            ? new TrayAuthResult(false, "SERVICE_UNAVAILABLE", null)
            : new TrayAuthResult(true, null, payload);
    }

    private sealed record ExchangeRequestBody(
        string Code, string DeviceName, string DeviceOs, string DeviceFingerprint);

    private sealed record RefreshRequestBody(string RefreshToken, string DeviceFingerprint);

    private sealed record PendingNotificationsResponse(
        [property: JsonPropertyName("notifications")] List<PendingNotificationPayload> Notifications);
}

public sealed record PendingNotificationPayload(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Wire-format mirror of the backend's TrayAuthResponseDto.</summary>
public sealed record TrayAuthPayload(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds,
    [property: JsonPropertyName("employee_name")] string? EmployeeName,
    [property: JsonPropertyName("employee_email")] string? EmployeeEmail,
    [property: JsonPropertyName("employee_number")] string? EmployeeNumber);

public sealed record TrayAuthResult(bool Success, string? ErrorCode, TrayAuthPayload? Auth);

/// <summary>
/// Wire-format mirror of the backend's tray-policy response (snake_case). Kept separate from
/// <see cref="AgentPolicy"/> — that internal PascalCase contract is also used for the IPC
/// PolicyPush wire format, which must not gain HTTP-only JsonPropertyName mappings.
/// </summary>
public sealed record TrayAgentPolicyPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil);

public sealed record PolicyResult(bool Success, string? ErrorCode, AgentPolicy? Policy);

/// <summary>Wire-format mirror of the backend's EnrollmentAttemptResponseDto.</summary>
public sealed record EnrollmentAttemptPayload(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);

public sealed record EnrollmentAttemptResult(bool Success, string? ErrorCode, EnrollmentAttemptPayload? Attempt);

/// <summary>Wire-format mirror of the backend's BiometricProfileResponseDto.</summary>
public sealed record BiometricProfilePayload(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enrolled_at")] DateTimeOffset EnrolledAt);

public sealed record CompleteEnrollmentResult(bool Success, string? ErrorCode, string? ProfileStatus);
