using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.Collectors;

public class DeviceStateCollectorTests
{
    private static AgentPolicy Policy(bool locationTrackingEnabled) => new()
    {
        Version = "v1",
        LocationTrackingEnabled = locationTrackingEnabled,
        IdleThresholdMinutes = 2
    };

    [Fact]
    public async Task EmitSampleAsync_LocationTrackingDisabled_NeverRequestsLocation()
    {
        var location = new FakeLocationService(LocationCaptureResult.Success(
            new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow)));
        var pipe = new FakeNamedPipeClient();
        var collector = new DeviceStateCollector(
            NullLogger<DeviceStateCollector>.Instance, pipe, new FakeSessionDayMetrics(), location);

        await collector.StartAsync(Policy(locationTrackingEnabled: false), CancellationToken.None);
        await collector.EmitSampleForTestAsync(CancellationToken.None);
        for (var i = 0; i < 20; i++)
            await collector.EmitSampleForTestAsync(CancellationToken.None);

        Assert.Equal(0, location.CallCount);
        Assert.All(pipe.SubmittedDeviceStateSnapshots, s => Assert.Null(s.Latitude));
    }

    [Fact]
    public async Task EmitSampleAsync_LocationTrackingEnabled_RequestsFixOnlyEveryFifteenthTick()
    {
        var fix = new GeoLocationFix(6.9271, 79.8612, 15, DateTimeOffset.UtcNow);
        var location = new FakeLocationService(LocationCaptureResult.Success(fix));
        var pipe = new FakeNamedPipeClient();
        var collector = new DeviceStateCollector(
            NullLogger<DeviceStateCollector>.Instance, pipe, new FakeSessionDayMetrics(), location);

        await collector.StartAsync(Policy(locationTrackingEnabled: true), CancellationToken.None);
        for (var i = 0; i < 15; i++)
            await collector.EmitSampleForTestAsync(CancellationToken.None);

        Assert.Equal(1, location.CallCount);
        var located = pipe.SubmittedDeviceStateSnapshots.Single(s => s.Latitude is not null);
        Assert.Equal(6.9271, located.Latitude);
        Assert.Equal(14, pipe.SubmittedDeviceStateSnapshots.Count(s => s.Latitude is null));
    }
}
