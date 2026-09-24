namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class PeriodicScreenshotCollectorTests
{
    private static AgentPolicy Policy(bool screenshots) => new()
    {
        Version = "v1",
        ActivitySignalEnabled = true,
        AppUsageEnabled = false,
        ScreenshotEnabled = screenshots,
        CameraVerificationEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task DisabledPolicy_DoesNotCapture()
    {
        var pipe = new FakeNamedPipeClient();
        var capture = new FakeScreenshotCaptureService();
        var sut = new PeriodicScreenshotCollector(
            NullLogger<PeriodicScreenshotCollector>.Instance,
            capture,
            pipe,
            TimeSpan.Zero,
            TimeSpan.FromHours(1));

        await sut.StartAsync(Policy(screenshots: false), CancellationToken.None);
        await Task.Delay(50);

        Assert.False(sut.IsRunning);
        Assert.Empty(pipe.PeriodicScreenshotBytes);
        Assert.Equal(0, capture.CallCount);
    }

    [Fact]
    public async Task EnabledPolicy_UploadsTheFirstCapture()
    {
        var pipe = new FakeNamedPipeClient();
        var capture = new FakeScreenshotCaptureService
        {
            NextResult = new(true, new byte[] { 9, 8, 7 }, DateTimeOffset.UtcNow, 1, default, "hash", null)
        };
        var sut = new PeriodicScreenshotCollector(
            NullLogger<PeriodicScreenshotCollector>.Instance,
            capture,
            pipe,
            TimeSpan.Zero,
            TimeSpan.FromHours(1));

        await sut.StartAsync(Policy(screenshots: true), CancellationToken.None);
        var started = DateTime.UtcNow;
        while (pipe.PeriodicScreenshotBytes.Count == 0 && DateTime.UtcNow - started < TimeSpan.FromSeconds(2))
            await Task.Delay(20);
        await sut.StopAsync(CancellationToken.None);

        Assert.Equal([3], pipe.PeriodicScreenshotBytes);
    }
}
