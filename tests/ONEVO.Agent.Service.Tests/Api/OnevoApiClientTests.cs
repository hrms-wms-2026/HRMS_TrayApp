namespace ONEVO.Agent.Service.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using Xunit;

public class OnevoApiClientTests
{
    [Fact]
    public async Task StartDeviceAuthorizationAsync_SendsMetadataAndParsesResponse()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    device_code = "device-secret",
                    user_code = "ABCD2345",
                    verification_uri = "https://app.example.com/device/activate",
                    verification_uri_complete = "https://app.example.com/device/activate?request_id=id&user_code=ABCD2345",
                    expires_in_seconds = 600,
                    interval_seconds = 5,
                })
            };
        });
        var client = Build(handler);

        var result = await client.StartDeviceAuthorizationAsync("Laptop", "Windows 11", "1.0.0", "fingerprint", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("device-secret", result.DeviceCode);
        Assert.Equal("ABCD2345", result.UserCode);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(AgentApiRoutes.DeviceAuthorizationStart, captured.RequestUri!.AbsolutePath);
        Assert.Null(captured.Headers.Authorization);
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("device_name", body);
        Assert.DoesNotContain("Authorization", body);
    }

    [Fact]
    public async Task PollDeviceAuthorizationAsync_PendingParsesCodeAndKeepsDeviceCodeOutOfUrl()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { code = "authorization_pending" })
            };
        });
        var client = Build(handler);

        var result = await client.PollDeviceAuthorizationAsync("device-secret", "fingerprint", CancellationToken.None);

        Assert.Equal(DeviceAuthorizationPollState.AuthorizationPending, result.State);
        Assert.DoesNotContain("device-secret", captured!.RequestUri!.ToString());
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("device-secret", body);
        Assert.Contains("fingerprint", body);
    }

    [Fact]
    public async Task PollDeviceAuthorizationAsync_AuthorizedParsesTrayAuthPayload()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                access_token = "access",
                expires_in_seconds = 3600,
                refresh_token = "refresh",
                refresh_expires_in_seconds = 7_776_000,
            })
        });
        var client = Build(handler);

        var result = await client.PollDeviceAuthorizationAsync("device-secret", "fingerprint", CancellationToken.None);

        Assert.Equal(DeviceAuthorizationPollState.Authorized, result.State);
        Assert.Equal("access", result.Auth!.AccessToken);
    }

    [Fact]
    public async Task SendHeartbeatAsync_SendsBearerTokenAndReturnsSuccess()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = Build(handler);

        var success = await client.SendHeartbeatAsync("access-token", CancellationToken.None);

        Assert.True(success);
        Assert.Equal(AgentApiRoutes.ActivationHeartbeat, captured!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("access-token", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ExchangeActivationCodeAsync_Success_ReturnsAuthPayload()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                access_token = "eyJ.test",
                expires_in_seconds = 3600,
                refresh_token = "raw-refresh",
                refresh_expires_in_seconds = 7_776_000,
                employee_name = "Priya Employee",
                employee_email = "priya@test.dev",
                employee_number = "EMP-0001"
            })
        });
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("ABC12345", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal("eyJ.test", result.Auth!.AccessToken);
        Assert.Equal("raw-refresh", result.Auth.RefreshToken);
        Assert.Equal(3600, result.Auth.ExpiresInSeconds);
        Assert.Equal("Priya Employee", result.Auth.EmployeeName);
        Assert.Equal("priya@test.dev", result.Auth.EmployeeEmail);
        Assert.Equal("EMP-0001", result.Auth.EmployeeNumber);
    }

    [Fact]
    public async Task ExchangeActivationCodeAsync_Unauthorized_ReturnsUnauthorizedErrorCode()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("BADCODE1", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        Assert.Null(result.Auth);
    }

    [Fact]
    public async Task ExchangeActivationCodeAsync_ServerError_ReturnsServiceUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("ABC12345", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task ExchangeActivationCodeAsync_NetworkFailure_ReturnsServiceUnavailable_NeverThrows()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("ABC12345", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task RefreshTokenAsync_Success_ReturnsRotatedTokens()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                access_token = "eyJ.new",
                expires_in_seconds = 3600,
                refresh_token = "new-refresh",
                refresh_expires_in_seconds = 7_776_000
            })
        });
        var client = Build(handler);

        var result = await client.RefreshTokenAsync("old-refresh", "fp-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("new-refresh", result.Auth!.RefreshToken);
    }

    [Fact]
    public async Task RevokeDeviceAsync_Success_ReturnsTrue()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Build(handler);

        var success = await client.RevokeDeviceAsync("access-token", CancellationToken.None);

        Assert.True(success);
    }

    [Fact]
    public async Task RevokeDeviceAsync_Failure_ReturnsFalse_NeverThrows()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var client = Build(handler);

        var success = await client.RevokeDeviceAsync("access-token", CancellationToken.None);

        Assert.False(success);
    }

    private static OnevoApiClient Build(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
