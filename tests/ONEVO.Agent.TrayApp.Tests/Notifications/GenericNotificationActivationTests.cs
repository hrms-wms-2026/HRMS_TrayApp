namespace ONEVO.Agent.TrayApp.Tests.Notifications;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

public sealed class GenericNotificationActivationTests
{
    private const string Portal = "https://acme.onexso.com:4200";

    private static GenericNotificationPushPayload Payload(string actionKey) => new()
    {
        DeliveryId = Guid.NewGuid(), EventKey = "work_approval_requested", Title = "t", Message = "m",
        ActionKey = actionKey, RelatedEntityType = "work_approval_request", RelatedEntityId = Guid.NewGuid()
    };

    [Fact]
    public void OpenRelated_RoundTrips_ToThePortalDashboard()
    {
        var args = GenericNotificationActivation.BuildArguments(Payload("open_related"));

        var url = GenericNotificationActivation.TryGetNavigationUrl(args, Portal);

        // The web app resolves the item from the bell and enforces access; the tray never builds record links.
        Assert.Equal($"{Portal}/dashboard", url);
    }

    [Fact]
    public void NoAction_HasNoNavigation()
    {
        Assert.Null(GenericNotificationActivation.TryGetNavigationUrl(
            GenericNotificationActivation.BuildArguments(Payload("none")), Portal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("attempt=3f1b0a5e-0000-0000-0000-000000000000;decision=allow")]
    [InlineData("kind=generic;delivery=not-a-guid;action=open_related")]
    [InlineData("kind=generic;delivery=3f1b0a5e-0000-0000-0000-000000000000;action=run_script")]
    [InlineData("kind=generic;delivery=3f1b0a5e-0000-0000-0000-000000000000;action=open_related;url=javascript:alert(1)")]
    public void UnknownOrMalformedArguments_AreIgnored_OrNeverUseAClientSuppliedUrl(string args)
    {
        var url = GenericNotificationActivation.TryGetNavigationUrl(args, Portal);

        Assert.True(url is null || url == $"{Portal}/dashboard");
    }

    [Fact]
    public void NonHttpPortal_IsRefused()
    {
        var args = GenericNotificationActivation.BuildArguments(Payload("open_related"));

        Assert.Null(GenericNotificationActivation.TryGetNavigationUrl(args, "file:///C:/Windows"));
    }

    [Fact]
    public void IsGeneric_DistinguishesFromInactivityPromptArguments()
    {
        Assert.True(GenericNotificationActivation.IsGeneric(GenericNotificationActivation.BuildArguments(Payload("open_related"))));
        Assert.False(GenericNotificationActivation.IsGeneric("attempt=3f1b0a5e-0000-0000-0000-000000000000;decision=allow"));
    }
}
