namespace ONEVO.Agent.Service.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

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
}

/// <summary>Wire-format mirror of the backend's TrayAuthResponseDto.</summary>
public sealed record TrayAuthPayload(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("refresh_expires_in_seconds")] int RefreshExpiresInSeconds);

public sealed record TrayAuthResult(bool Success, string? ErrorCode, TrayAuthPayload? Auth);
