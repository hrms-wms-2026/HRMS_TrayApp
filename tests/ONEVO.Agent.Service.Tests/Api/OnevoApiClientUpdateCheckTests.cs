namespace ONEVO.Agent.Service.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using Xunit;

public class OnevoApiClientUpdateCheckTests
{
    [Fact]
    public async Task CheckForUpdate_MapsUpdateAvailableResponse()
    {
        HttpRequestMessage? captured = null;
        var client = Build(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    update_available = true,
                    mandatory = true,
                    latest = new
                    {
                        version = "1.3.0",
                        download_url = "https://dl.example.com/ONEVO-1.3.0.msix",
                        sha256 = new string('a', 64),
                        file_size_bytes = 123,
                        release_notes = "n"
                    }
                })
            };
        }));

        var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.UpdateAvailable);
        Assert.True(result.Mandatory);
        Assert.Equal("1.3.0", result.LatestVersion);
        Assert.Equal("https://dl.example.com/ONEVO-1.3.0.msix", result.DownloadUrl);
        Assert.Equal(123, result.FileSizeBytes);
        Assert.EndsWith("/api/v1/tray/releases/check?current=1.0.0&channel=stable", captured!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task CheckForUpdate_NoUpdate_ReturnsSuccessWithoutLatest()
    {
        var client = Build(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { update_available = false, mandatory = false, latest = (object?)null })
        }));

        var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdate_HttpFailure_ReturnsSuccessFalse_NeverThrows()
    {
        var client = Build(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("HTTP_500", result.ErrorCode);
    }

    [Fact]
    public async Task CheckForUpdate_NetworkException_ReturnsSuccessFalse_NeverThrows()
    {
        var client = Build(new StubHandler(_ => throw new HttpRequestException("down")));

        var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task CheckForUpdate_RejectsHttpDownloadUrl()
    {
        var client = Build(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                update_available = true,
                mandatory = false,
                latest = new
                {
                    version = "1.3.0",
                    download_url = "http://insecure/x.msix",
                    sha256 = new string('a', 64),
                    file_size_bytes = 1,
                    release_notes = (string?)null
                }
            })
        }));

        var result = await client.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSECURE_URL", result.ErrorCode);
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
