using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using Xunit;

#pragma warning disable CA1001

namespace ONEVO.Agent.Service.Tests.Sync;

[Collection(CredentialStoreFileCollection.Name)]
public class AttendanceStatusSyncServiceTests
{
    [Fact]
    public async Task PollOnceAsync_BackendReportsClockedIn_CallsApplyPresenceActive()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { is_clocked_in = true, clocked_in_at_utc = DateTimeOffset.UtcNow })
        });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var applyActiveCalled = false;
        var reconciler = new RecordingPresenceReconciler(onApplyActive: () => applyActiveCalled = true);
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            apiClient,
            new CredentialStore(),
            reconciler);

        await sut.PollOnceAsync("device-jwt", CancellationToken.None);

        Assert.True(applyActiveCalled);
        Assert.Equal(["Active"], reconciler.Calls);
    }

    [Fact]
    public async Task PollOnceAsync_BackendReportsNotClockedIn_CallsApplyPresenceStopped()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { is_clocked_in = false, clocked_in_at_utc = (DateTimeOffset?)null })
        });
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var reconciler = new RecordingPresenceReconciler();
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            apiClient,
            new CredentialStore(),
            reconciler);

        await sut.PollOnceAsync("device-jwt", CancellationToken.None);

        Assert.Equal(["Stopped"], reconciler.Calls);
    }

    [Fact]
    public async Task PollOnceAsync_BackendCallFails_DoesNotChangeState()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var apiClient = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        var reconciler = new RecordingPresenceReconciler();
        var sut = new AttendanceStatusSyncService(
            NullLogger<AttendanceStatusSyncService>.Instance,
            apiClient,
            new CredentialStore(),
            reconciler);

        await sut.PollOnceAsync("device-jwt", CancellationToken.None);

        Assert.Empty(reconciler.Calls);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
