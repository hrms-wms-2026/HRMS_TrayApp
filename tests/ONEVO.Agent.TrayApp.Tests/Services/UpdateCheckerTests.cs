namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task Check_ReturnsResult_WhenUpdateAvailable()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult = new UpdateCheckResultPayload(
            true, true, false, "1.3.0", "https://dl.example.com/a.msix", new string('a', 64), 10, null, null) };
        var checker = new UpdateChecker(pipe, () => "1.0.0");

        var result = await checker.CheckAsync(CancellationToken.None);

        Assert.True(result!.UpdateAvailable);
        Assert.Equal("1.3.0", result.LatestVersion);
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenServiceDidNotAnswer()
    {
        var checker = new UpdateChecker(new FakeNamedPipeClient { NextUpdateCheckResult = null }, () => "1.0.0");
        Assert.Null(await checker.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenCheckFailed()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult =
            new UpdateCheckResultPayload(false, false, false, null, null, null, 0, null, "SERVICE_UNAVAILABLE") };
        Assert.Null(await new UpdateChecker(pipe, () => "1.0.0").CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Check_ReturnsNull_WhenNoUpdate()
    {
        var pipe = new FakeNamedPipeClient { NextUpdateCheckResult =
            new UpdateCheckResultPayload(true, false, false, null, null, null, 0, null, null) };
        Assert.Null(await new UpdateChecker(pipe, () => "1.0.0").CheckAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("1.2.3.0", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("10.0.4.7", "10.0.4")]
    public void ToThreePartVersion_DropsThePackageRevision(string input, string expected)
    {
        Assert.Equal(expected, UpdateChecker.ToThreePartVersion(input));
    }
}
