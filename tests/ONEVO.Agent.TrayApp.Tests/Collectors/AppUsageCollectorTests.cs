using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.Collectors;

public class AppUsageCollectorTests
{
    private static AgentPolicy Policy(int idleThresholdMinutes) => new()
    {
        Version = "v1",
        AppUsageEnabled = true,
        IdleThresholdMinutes = idleThresholdMinutes
    };

    private static async Task<(AppUsageCollector Collector, SessionDayMetrics Metrics)> StartedCollector(
        int idleSeconds, int idleThresholdMinutes)
    {
        var metrics = new SessionDayMetrics();
        var collector = new AppUsageCollector(
            NullLogger<AppUsageCollector>.Instance,
            new FakeNamedPipeClient(),
            metrics,
            NullAppIconCache.Instance,
            new SettableIdleTimeProvider(idleSeconds));
        await collector.StartAsync(Policy(idleThresholdMinutes), CancellationToken.None);
        return (collector, metrics);
    }

    [Fact]
    public async Task CountTowardDay_UserActive_AddsAppTime()
    {
        var (collector, metrics) = await StartedCollector(idleSeconds: 30, idleThresholdMinutes: 2);

        var counted = collector.CountTowardDay("code.exe");

        Assert.True(counted);
        var app = Assert.Single(metrics.GetTopApps());
        Assert.Equal("code.exe", app.Name);
        Assert.Equal(TimeSpan.FromSeconds(60), app.Duration);
    }

    [Fact]
    public async Task CountTowardDay_UserIdlePastPolicyThreshold_SkipsAppTime()
    {
        var (collector, metrics) = await StartedCollector(idleSeconds: 180, idleThresholdMinutes: 2);

        var counted = collector.CountTowardDay("chrome.exe");

        Assert.False(counted);
        Assert.Empty(metrics.GetTopApps());
        Assert.Empty(metrics.GetHourlyFocusFractions());
    }

    [Fact]
    public async Task CountTowardDay_UsesPolicyThreshold_NotTheLocalDefault()
    {
        // 3 minutes without input is past the 2-minute local default but inside a 15-minute policy.
        var (collector, metrics) = await StartedCollector(idleSeconds: 180, idleThresholdMinutes: 15);

        Assert.True(collector.CountTowardDay("code.exe"));
        Assert.Single(metrics.GetTopApps());
    }

    [Fact]
    public async Task CountTowardDay_ZeroPolicyThreshold_FallsBackToDefault()
    {
        var (collector, metrics) = await StartedCollector(idleSeconds: 30, idleThresholdMinutes: 0);

        Assert.True(collector.CountTowardDay("code.exe"));
        Assert.Single(metrics.GetTopApps());
    }

    [Fact]
    public async Task CountTowardDay_BlankProcessName_AddsNothing()
    {
        var (collector, metrics) = await StartedCollector(idleSeconds: 0, idleThresholdMinutes: 2);

        Assert.False(collector.CountTowardDay(" "));
        Assert.Empty(metrics.GetTopApps());
    }

    private sealed class SettableIdleTimeProvider(int idleSeconds) : IIdleTimeProvider
    {
        public int GetIdleSeconds() => idleSeconds;
    }
}
