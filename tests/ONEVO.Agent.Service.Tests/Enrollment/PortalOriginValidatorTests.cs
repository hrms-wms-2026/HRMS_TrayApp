using ONEVO.Agent.Service.Enrollment;

namespace ONEVO.Agent.Service.Tests.Enrollment;

public sealed class PortalOriginValidatorTests
{
    private static PortalOriginValidator Create(bool development = false) => new(
        "example.com",
        443,
        [4200],
        ["https://localhost:4200", "https://acme.localhost:4200"],
        development);

    [Theory]
    [InlineData("https://tenant.example.com", "https://tenant.example.com")]
    [InlineData("https://example.com/", "https://example.com")]
    public void Production_origin_is_normalized(string value, string expected)
    {
        var result = Create().TryNormalize(value, out var normalized);
        Assert.True(result);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("http://tenant.example.com")]
    [InlineData("https://tenant.example.com:444")]
    [InlineData("https://tenant.example.com/dashboard")]
    [InlineData("https://user:password@tenant.example.com")]
    [InlineData("https://tenant.evil-example.com")]
    [InlineData("https://tenant.example.com?x=1")]
    public void Invalid_production_origin_is_rejected(string value)
    {
        Assert.False(Create().TryNormalize(value, out _));
    }

    [Fact]
    public void Development_origin_requires_explicit_allowlist()
    {
        Assert.True(Create(development: true).TryNormalize("https://acme.localhost:4200", out _));
        Assert.False(Create(development: true).TryNormalize("https://other.localhost:4200", out _));
    }
}
