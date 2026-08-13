namespace ONEVO.Agent.Service.Tests.Biometrics;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Security;
using Xunit;

/// <summary>
/// CredentialStore reads/writes real DPAPI-protected files under %ProgramData%\ONEVO\Agent\,
/// the same location the real Service uses — each test clears what it wrote in `finally` so it
/// doesn't leak state into other tests or a real dev Service running on this machine (same
/// convention as CredentialStoreTests). OnevoApiClient is exercised against a StubHandler rather
/// than mocked — this test project has no mocking library, matching OnevoApiClientTests.
/// </summary>
public class EnrollmentCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WhenNoDeviceJwt_ReturnsFailure()
    {
        var credentials = new CredentialStore();
        credentials.ClearDeviceJwt();
        var coordinator = Build(credentials, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NO_DEVICE_CREDENTIAL", result.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_WithDeviceJwt_ReturnsAwsSessionDetails()
    {
        var credentials = new CredentialStore();
        try
        {
            credentials.StoreDeviceJwt("device-jwt");
            var attemptId = Guid.NewGuid();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    attempt_id = attemptId,
                    aws_session_id = "aws-session-1",
                    region = "ap-south-1",
                    challenge_type = "FaceMovementAndLightChallenge",
                    access_key_id = "AKIA",
                    secret_access_key = "secret",
                    session_token = "token",
                    credentials_expire_at = DateTimeOffset.UtcNow.AddMinutes(15)
                })
            });
            var coordinator = Build(credentials, handler);

            var result = await coordinator.StartAsync(CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(attemptId, result.AttemptId);
            Assert.Equal("aws-session-1", result.AwsSessionId);
            Assert.Equal("ap-south-1", result.Region);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task StartAsync_WhenBackendUnauthorized_ReturnsErrorCode()
    {
        var credentials = new CredentialStore();
        try
        {
            credentials.StoreDeviceJwt("device-jwt");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var coordinator = Build(credentials, handler);

            var result = await coordinator.StartAsync(CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("UNAUTHORIZED", result.ErrorCode);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenNoDeviceJwt_ReturnsFailure()
    {
        var credentials = new CredentialStore();
        credentials.ClearDeviceJwt();
        var coordinator = Build(credentials, new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await coordinator.CompleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NO_DEVICE_CREDENTIAL", result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_WithDeviceJwt_ReturnsProfileStatus()
    {
        var credentials = new CredentialStore();
        try
        {
            credentials.StoreDeviceJwt("device-jwt");
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    profile_id = Guid.NewGuid(),
                    status = "active",
                    enrolled_at = DateTimeOffset.UtcNow
                })
            });
            var coordinator = Build(credentials, handler);

            var result = await coordinator.CompleteAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("active", result.ProfileStatus);
        }
        finally
        {
            credentials.ClearDeviceJwt();
        }
    }

    private static EnrollmentCoordinator Build(CredentialStore credentials, HttpMessageHandler handler)
    {
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        return new EnrollmentCoordinator(NullLogger<EnrollmentCoordinator>.Instance, apiClient, credentials);
    }

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
