namespace ONEVO.Agent.Service.Tests.Sync;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using Xunit;

[Collection(CredentialStoreFileCollection.Name)]
public class NotificationPollingServiceTests : IDisposable
{
    private static readonly Guid LegacyId = Guid.NewGuid();
    private static readonly Guid DeliveryId = Guid.NewGuid();
    private static readonly Guid RelatedId = Guid.NewGuid();

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly RecordingBroadcaster _broadcaster = new();

    public void Dispose() => TestStores.NewCredentialStore().ClearDeviceJwt();

    private NotificationPollingService Build(string legacyJson, string genericJson)
    {
        var credentials = TestStores.NewCredentialStore();
        credentials.StoreDeviceJwt("test-device-jwt");
        var handler = new StubHandler(request =>
        {
            _requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path == AgentApiRoutes.PendingTrayNotifications)
                return Json(legacyJson);
            if (path == AgentApiRoutes.PendingTrayDeliveries)
                return Json(genericJson);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var api = new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance);
        return new NotificationPollingService(NullLogger<NotificationPollingService>.Instance, api, credentials, _broadcaster);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string GenericBody(DateTimeOffset expiresAt) => $$"""
        {"deliveries":[{"deliveryId":"{{DeliveryId}}","eventKey":"work_approval_requested","title":"Approval needed",
          "message":"Bala asked","actionKey":"open_related","relatedEntityType":"work_approval_request",
          "relatedEntityId":"{{RelatedId}}","expiresAt":"{{expiresAt:O}}"}]}
        """;

    [Fact]
    public async Task GenericDeliveries_AreForwardedToTheTray_ButNotAcknowledgedByTheService()
    {
        var service = Build("""{"notifications":[]}""", GenericBody(DateTimeOffset.UtcNow.AddHours(1)));

        await service.PollOnceAsync(CancellationToken.None);

        var push = Assert.Single(_broadcaster.Broadcasts, e => e.Type == IpcMessageTypes.GenericNotificationPush);
        var payload = push.Payload!.Value.Deserialize<GenericNotificationPushPayload>()!;
        Assert.Equal(DeliveryId, payload.DeliveryId);
        Assert.Equal("Approval needed", payload.Title);
        Assert.Equal("open_related", payload.ActionKey);
        Assert.Equal(RelatedId, payload.RelatedEntityId);
        // Acknowledgement waits for the tray process to accept the toast (GenericNotificationAccepted).
        Assert.DoesNotContain(_requests, r => r.RequestUri!.AbsolutePath.Contains("/deliveries/") && r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task LegacyAlerts_KeepTheirExistingForwardAndAckBehaviour()
    {
        var legacy = $$"""{"notifications":[{"id":"{{LegacyId}}","type":"BreakReminder","title":"Break","message":"Take a break"}]}""";
        var service = Build(legacy, """{"deliveries":[]}""");

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Single(_broadcaster.Broadcasts, e => e.Type == IpcMessageTypes.NotificationPush);
        Assert.Contains(_requests, r => r.Method == HttpMethod.Post
            && r.RequestUri!.AbsolutePath == string.Format(AgentApiRoutes.AckTrayNotification, LegacyId));
    }

    [Fact]
    public async Task GenericEndpointUnavailable_DoesNotBreakLegacyDelivery()
    {
        var legacy = $$"""{"notifications":[{"id":"{{LegacyId}}","type":"BreakReminder","title":"Break","message":"Take a break"}]}""";
        var service = Build(legacy, "not json");

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Single(_broadcaster.Broadcasts, e => e.Type == IpcMessageTypes.NotificationPush);
        Assert.DoesNotContain(_broadcaster.Broadcasts, e => e.Type == IpcMessageTypes.GenericNotificationPush);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.example.com/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class RecordingBroadcaster : IIpcBroadcaster
    {
        public List<IpcEnvelope> Broadcasts { get; } = [];

        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
        {
            Broadcasts.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
