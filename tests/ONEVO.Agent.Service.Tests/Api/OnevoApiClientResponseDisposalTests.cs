using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Shared.IPC;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Api;

/// <summary>
/// AUD-015: many OnevoApiClient methods never disposed the HttpResponseMessage, so the response
/// (and its pooled connection) waited for the GC in an all-day process. Every call — success or
/// error — must dispose what it received.
/// </summary>
public sealed class OnevoApiClientResponseDisposalTests
{
    private const string Jwt = "device-jwt";

    public static TheoryData<string, HttpStatusCode> Calls()
    {
        var data = new TheoryData<string, HttpStatusCode>();
        foreach (var name in Invocations.Keys)
        {
            data.Add(name, HttpStatusCode.OK);
            data.Add(name, HttpStatusCode.InternalServerError);
        }
        return data;
    }

    private static readonly Dictionary<string, Func<OnevoApiClient, Task>> Invocations = new()
    {
        ["ExchangeActivationCode"] = c => c.ExchangeActivationCodeAsync("ABCDEFGH", "pc", "Windows", "fp", default),
        ["RefreshToken"] = c => c.RefreshTokenAsync("refresh", "fp", default),
        ["StartDeviceAuthorization"] = c => c.StartDeviceAuthorizationAsync("pc", "Windows", "1.0", "fp", default),
        ["PollDeviceAuthorization"] = c => c.PollDeviceAuthorizationAsync("code", "fp", default),
        ["SendHeartbeat"] = c => c.SendHeartbeatAsync(Jwt, default),
        ["RevokeDevice"] = c => c.RevokeDeviceAsync(Jwt, default),
        ["CheckForUpdate"] = c => c.CheckForUpdateAsync("1.0.0", default),
        ["CompleteLegalAcceptance"] = c => c.CompleteLegalAcceptanceAsync(
            "challenge", "csrf", [new LegalAcceptanceItemPayload("terms", "1")], default),
        ["GetEffectivePolicy"] = c => c.GetEffectivePolicyAsync(Jwt, default),
        ["GetAttendanceStatus"] = c => c.GetAttendanceStatusAsync(Jwt, default),
        ["GetDailyTasks"] = c => c.GetDailyTasksAsync(Jwt, default),
        ["ClockIn"] = c => c.ClockInAsync(Jwt, default),
        ["ClockOut"] = c => c.ClockOutAsync(Jwt, default),
        ["StartBreak"] = c => c.StartBreakAsync(Jwt, default),
        ["EndBreak"] = c => c.EndBreakAsync(Jwt, default),
        ["GetPendingNotifications"] = c => c.GetPendingNotificationsAsync(Jwt, default),
        ["AckNotification"] = c => c.AckNotificationAsync(Jwt, Guid.NewGuid(), default),
        ["GetPendingTrayDeliveries"] = c => c.GetPendingTrayDeliveriesAsync(Jwt, default),
        ["AckTrayDelivery"] = c => c.AckTrayDeliveryAsync(Jwt, Guid.NewGuid(), default),
        ["CreateEnrollmentAttempt"] = c => c.CreateEnrollmentAttemptAsync(Jwt, default),
        ["CompleteEnrollmentAttempt"] = c => c.CompleteEnrollmentAttemptAsync(Jwt, Guid.NewGuid(), default),
        ["ValidateFacePhoto"] = c => c.ValidateFacePhotoAsync(Jwt, "jpeg", [1, 2, 3], null, null, default),
        ["GetFaceReferenceStatus"] = c => c.GetFaceReferenceStatusAsync(Jwt, default),
        ["GetPermissionConsent"] = c => c.GetPermissionConsentAsync(Jwt, default),
        ["RecordPermissionConsent"] = c => c.RecordPermissionConsentAsync(
            Jwt, new PermissionConsentPayload(true, true, true, true, true, true), default),
        ["EnrollFacePhotos"] = c => c.EnrollFacePhotosAsync(Jwt, [1, 2, 3], default),
        ["SubmitLocationChangeRequest"] = c => c.SubmitLocationChangeRequestAsync(Jwt, 1, 2, 3, "moved", default),
        ["ConfirmWorkLocation"] = c => c.ConfirmWorkLocationAsync(Jwt, "office", 1, 2, 3, default),
        ["RespondToLocationChangeRequest"] = c => c.RespondToLocationChangeRequestAsync(Jwt, Guid.NewGuid(), true, default),
        ["GetPendingLocationChangeDecision"] = c => c.GetPendingLocationChangeDecisionAsync(Jwt, default),
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task EveryCall_DisposesTheResponse(string call, HttpStatusCode status)
    {
        var handler = new TrackingHandler(status);
        var client = new OnevoApiClient(new SingleHandlerFactory(handler), NullLogger<OnevoApiClient>.Instance);

        await Invocations[call](client);

        Assert.NotEmpty(handler.Contents);
        Assert.All(handler.Contents, content => Assert.True(content.Disposed, $"{call} ({(int)status}) left a response undisposed"));
    }

    private sealed class TrackingContent() : StringContent("{}", Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<TrackingContent> Contents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new TrackingContent();
            Contents.Add(content);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.example.com/") };
    }
}
