namespace ONEVO.Agent.Service.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using Xunit;

public class OnevoApiClientTests
{
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
                refresh_expires_in_seconds = 7_776_000
            })
        });
        var client = Build(handler);

        var result = await client.ExchangeActivationCodeAsync("ABC12345", "Laptop", "Windows", "fp-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal("eyJ.test", result.Auth!.AccessToken);
        Assert.Equal("raw-refresh", result.Auth.RefreshToken);
        Assert.Equal(3600, result.Auth.ExpiresInSeconds);
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
