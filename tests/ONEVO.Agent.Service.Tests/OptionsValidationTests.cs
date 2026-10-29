namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service.Configuration;
using Xunit;

public class OptionsValidationTests
{
    [Theory]
    [InlineData("")]                               // not configured yet: sync keeps records locally
    [InlineData("https://api.uat.example.com")]
    [InlineData("https://api.uat.example.com:8443/")]
    public void ApiBaseUrl_Accepted(string url)
    {
        var result = new OptionsValidation().Validate(null, new AgentOptions { ApiBaseUrl = url });
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData("http://api.uat.example.com")]     // ISS-019: bearer token would travel in clear text
    [InlineData("api.uat.example.com")]
    [InlineData("/api")]
    [InlineData("ftp://api.uat.example.com")]
    public void ApiBaseUrl_Rejected(string url)
    {
        var result = new OptionsValidation().Validate(null, new AgentOptions { ApiBaseUrl = url });
        Assert.True(result.Failed);
        Assert.Contains("ApiBaseUrl", result.FailureMessage);
    }

    [Fact]
    public void ShippedDefaults_AreValid()
    {
        Assert.True(new OptionsValidation().Validate(null, new AgentOptions()).Succeeded);
    }
}
