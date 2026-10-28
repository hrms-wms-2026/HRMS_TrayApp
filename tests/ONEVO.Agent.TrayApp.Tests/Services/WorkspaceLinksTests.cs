namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

[Collection(nameof(WorkspaceLinksTests))]
public sealed class WorkspaceLinksTests : IDisposable
{
    public WorkspaceLinksTests() => WorkspaceLinks.Reset();

    public void Dispose() => WorkspaceLinks.Reset();

    [Fact]
    public void BeforeAnyStatus_UsesFallback()
    {
        Assert.Equal(WorkspaceLinks.FallbackPortalUrl, WorkspaceLinks.PortalUrl);
        Assert.Equal(WorkspaceLinks.FallbackPortalUrl + "/dashboard", WorkspaceLinks.DashboardUrl);
    }

    [Theory]
    [InlineData("https://contoso.onexso.com", "https://contoso.onexso.com")]
    [InlineData("https://contoso.onexso.com:4200", "https://contoso.onexso.com:4200")]
    [InlineData("https://contoso.onexso.com/", "https://contoso.onexso.com")]
    public void HttpsOrigin_IsApplied(string pushed, string expected)
    {
        Assert.True(WorkspaceLinks.UseTenantPortal(pushed));
        Assert.Equal(expected, WorkspaceLinks.PortalUrl);
        Assert.Equal(expected + "/dashboard", WorkspaceLinks.DashboardUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://contoso.onexso.com")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://contoso.onexso.com/dashboard")]
    [InlineData("https://contoso.onexso.com?x=1")]
    [InlineData("https://contoso.onexso.com#frag")]
    [InlineData("https://user:pw@contoso.onexso.com")]
    [InlineData("custom-app://open")]
    public void AnythingButAnHttpsOrigin_IsIgnored_AndKeepsCurrentLinks(string? pushed)
    {
        WorkspaceLinks.UseTenantPortal("https://contoso.onexso.com");

        Assert.False(WorkspaceLinks.UseTenantPortal(pushed));
        Assert.Equal("https://contoso.onexso.com", WorkspaceLinks.PortalUrl);
    }
}
