namespace ONEVO.Agent.Service.Tests.Configuration;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ONEVO.Agent.Service.Configuration;
using Xunit;

public sealed class OptionsValidationTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ONEVO.Agent.Service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static AgentOptions Valid() => new()
    {
        ApiBaseUrl = "https://api.example.com",
        PortalRootDomain = "example.com",
    };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(AgentOptions options, string environment) =>
        new OptionsValidation(new FakeEnvironment(environment)).Validate(null, options);

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Uat")]
    public void ValidOptions_Pass(string environment)
    {
        Assert.True(Validate(Valid(), environment).Succeeded);
    }

    public static TheoryData<string, Action<AgentOptions>> BypassFlags() => new()
    {
        { nameof(AgentOptions.ForceMonitoringActive), o => o.ForceMonitoringActive = true },
        { nameof(AgentOptions.AllowLocalLifecycleWithoutFullGates), o => o.AllowLocalLifecycleWithoutFullGates = true },
        { nameof(AgentOptions.AllowIpcClientFromAnyPath), o => o.AllowIpcClientFromAnyPath = true },
    };

    [Theory]
    [MemberData(nameof(BypassFlags))]
    public void BypassFlag_OutsideDevelopment_FailsStartup(string flag, Action<AgentOptions> set)
    {
        var options = Valid();
        set(options);

        foreach (var environment in new[] { "Production", "Uat", "Staging" })
        {
            var result = Validate(options, environment);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Failures!, f => f.Contains(flag) && f.Contains(environment));
        }
    }

    [Theory]
    [MemberData(nameof(BypassFlags))]
    public void BypassFlag_InDevelopment_IsAllowed(string flag, Action<AgentOptions> set)
    {
        _ = flag;
        var options = Valid();
        set(options);

        Assert.True(Validate(options, "Development").Succeeded);
    }

    [Fact]
    public void ExistingRules_StillApply()
    {
        var options = Valid();
        options.ApiBaseUrl = "http://insecure.example.com";
        options.IngestIntervalSeconds = 5;

        var result = Validate(options, "Production");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("https://"));
        Assert.Contains(result.Failures!, f => f.Contains("IngestIntervalSeconds"));
    }
}
