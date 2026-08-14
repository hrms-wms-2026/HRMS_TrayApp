using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

#pragma warning disable CA1001 // test doubles create HttpClient; no disposal needed in test scope

namespace ONEVO.Agent.Service.Tests.Sync;

[Collection(CredentialStoreFileCollection.Name)]
public class PolicySyncServiceTests
{
    private static readonly object ValidPolicyBody = new
    {
        version = "policy-v2",
        activity_signal_enabled = true,
        app_usage_enabled = true,
        screenshot_enabled = true,
        inactivity_screenshot_enabled = true,
        camera_verification_enabled = false,
        valid_until = DateTimeOffset.UtcNow.AddHours(2)
    };

    private static PolicySyncService Build(
        HttpMessageHandler handler,
        PolicyCache? cache = null,
        RecordingBroadcaster? broadcaster = null) =>
        new(
            NullLogger<PolicySyncService>.Instance,
            new OnevoApiClient(new StubHttpClientFactory(handler), NullLogger<OnevoApiClient>.Instance),
            new CredentialStore(), // never touched by RefreshOnceAsync(jwt, ct) — jwt passed explicitly
            cache ?? new PolicyCache(),
            broadcaster ?? new RecordingBroadcaster());

    [Fact]
    public void RefreshInterval_LeavesMarginBeforeBackendPolicyValidity()
    {
        // Backend policy validity is a fixed 1 hour (see the tray-policy DTO this test file's
        // stub bodies mirror). The refresh cadence must stay strictly below that: the
        // PeriodicTimer in ExecuteAsync only starts counting once the prior fetch completes, so
        // a full-hour interval would always fire after the ValidUntil stamped from the backend's
        // earlier clock read, guaranteeing PolicyCache.Current degrades every cycle. This assertion
        // exists so a future edit can't silently reintroduce a zero/negative-margin interval.
        var backendValidityWindow = TimeSpan.FromHours(1);

        Assert.True(
            PolicySyncService.RefreshInterval < backendValidityWindow,
            $"RefreshInterval ({PolicySyncService.RefreshInterval}) must leave real margin before " +
            $"the backend's {backendValidityWindow} policy validity window, or every scheduled " +
            "refresh will race an already-expired policy.");
    }

    [Fact]
    public async Task RefreshOnceAsync_Success_UpdatesCacheAndBroadcastsOnce()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPolicyBody)
        });
        var cache = new PolicyCache();
        var broadcaster = new RecordingBroadcaster();
        var svc = Build(handler, cache, broadcaster);

        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.Equal("policy-v2", cache.Current.Version);
        Assert.True(cache.Current.ScreenshotEnabled);
        Assert.True(cache.Current.InactivityScreenshotEnabled);
        Assert.Single(broadcaster.Broadcasts);

        var pushed = broadcaster.Broadcasts[0];
        Assert.Equal(IpcMessageTypes.PolicyPush, pushed.Type);
        var payload = pushed.Payload!.Value.Deserialize<PolicyPushPayload>();
        Assert.Equal("policy-v2", payload!.Policy.Version);
    }

    [Fact]
    public async Task RefreshOnceAsync_SameVersionAgain_DoesNotBroadcastTwice()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPolicyBody)
        });
        var cache = new PolicyCache();
        var broadcaster = new RecordingBroadcaster();
        var svc = Build(handler, cache, broadcaster);

        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);
        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.Single(broadcaster.Broadcasts);
    }

    [Fact]
    public async Task RefreshOnceAsync_Unauthorized_LeavesLastValidPolicyUntouched()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cache = new PolicyCache();
        var lastValid = new AgentPolicy
        {
            Version = "still-valid",
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        cache.Set(lastValid);
        var broadcaster = new RecordingBroadcaster();
        var svc = Build(handler, cache, broadcaster);

        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.Equal("still-valid", cache.Current.Version);
        Assert.True(cache.Current.ScreenshotEnabled);
        Assert.Empty(broadcaster.Broadcasts);
    }

    [Fact]
    public async Task RefreshOnceAsync_Unauthorized_OnceCachedPolicyExpires_ScreenshotFlagsGoFalse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var cache = new PolicyCache();
        // Already past ValidUntil — simulates the backend having gone unreachable/401
        // for long enough that the last-known-good policy has run out its own clock.
        cache.Set(new AgentPolicy
        {
            Version = "expired-now",
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-5)
        });
        var svc = Build(handler, cache);

        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.False(cache.Current.ScreenshotEnabled);
        Assert.False(cache.Current.InactivityScreenshotEnabled);
        Assert.False(cache.Current.CameraVerificationEnabled);
    }

    [Fact]
    public async Task RefreshOnceAsync_NoJwt_MakesNoHttpCall()
    {
        var factory = new NeverCalledHttpClientFactory();
        var svc = new PolicySyncService(
            NullLogger<PolicySyncService>.Instance,
            new OnevoApiClient(factory, NullLogger<OnevoApiClient>.Instance),
            new CredentialStore(),
            new PolicyCache(),
            new RecordingBroadcaster());

        await svc.RefreshOnceAsync(null, CancellationToken.None);
        await svc.RefreshOnceAsync("   ", CancellationToken.None);

        // NeverCalledHttpClientFactory throws if CreateClient is ever invoked — reaching
        // here without an exception is the assertion.
    }

    [Fact]
    public async Task RefreshOnceAsync_BackendReturnsAlreadyExpiredPolicy_RejectedNotCached()
    {
        var expiredBody = new
        {
            version = "dead-on-arrival",
            activity_signal_enabled = true,
            app_usage_enabled = true,
            screenshot_enabled = true,
            inactivity_screenshot_enabled = true,
            camera_verification_enabled = true,
            valid_until = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expiredBody)
        });
        var cache = new PolicyCache();
        var broadcaster = new RecordingBroadcaster();
        var svc = Build(handler, cache, broadcaster);

        await svc.RefreshOnceAsync("device-jwt", CancellationToken.None);

        Assert.NotEqual("dead-on-arrival", cache.Current.Version);
        Assert.Empty(broadcaster.Broadcasts);
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

    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("HttpClient should not be called when no JWT is present");
    }

    private sealed class RecordingBroadcaster : IIpcBroadcaster
    {
        public List<IpcEnvelope> Broadcasts { get; } = new();

        public Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
        {
            Broadcasts.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
