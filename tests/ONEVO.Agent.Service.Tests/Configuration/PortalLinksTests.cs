namespace ONEVO.Agent.Service.Tests.Configuration;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Configuration;
using Xunit;

public sealed class PortalLinksTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ONEVO.Agent.Service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static PortalLinks Create(string environment, string root = "onexso.com", int productionPort = 443, int[]? devPorts = null) =>
        new(Options.Create(new AgentOptions
        {
            PortalRootDomain = root,
            PortalProductionPort = productionPort,
            PortalDevelopmentPorts = devPorts ?? [4200],
        }), new FakeEnvironment(environment));

    [Fact]
    public void Development_UsesTenantSubdomainAndFirstDevPort()
    {
        Assert.Equal("https://contoso.onexso.com:4200", Create("Development").BaseUrlFor("contoso"));
    }

    [Fact]
    public void OutsideDevelopment_UsesProductionPort_OmittingDefault443()
    {
        Assert.Equal("https://contoso.onexso.com", Create("Production").BaseUrlFor("contoso"));
        Assert.Equal("https://contoso.uat.example.com:8443",
            Create("Uat", root: "uat.example.com", productionPort: 8443).BaseUrlFor("contoso"));
    }

    [Fact]
    public void Development_WithNoDevPorts_FallsBackTo443()
    {
        Assert.Equal("https://contoso.onexso.com", Create("Development", devPorts: []).BaseUrlFor("contoso"));
    }

    [Theory]
    [InlineData("Contoso", "https://contoso.onexso.com")]
    [InlineData("  acme-2 ", "https://acme-2.onexso.com")]
    public void Slug_IsNormalised(string slug, string expected)
    {
        Assert.Equal(expected, Create("Production").BaseUrlFor(slug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme.evil.com")]
    [InlineData("acme/evil")]
    [InlineData("evil.com#")]
    [InlineData("ac me")]
    [InlineData("ünï")]
    public void UnsafeOrMissingSlug_GivesNoUrl(string? slug)
    {
        Assert.Null(Create("Production").BaseUrlFor(slug));
    }

    [Fact]
    public void OverlongSlug_GivesNoUrl()
    {
        Assert.Null(Create("Production").BaseUrlFor(new string('a', 64)));
        Assert.NotNull(Create("Production").BaseUrlFor(new string('a', 63)));
    }
}
