using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class ServiceConnectionStatusTests
{
    [Fact]
    public void StartsConnecting_UntilServiceSpeaks()
    {
        var status = new ServiceConnectionStatus(new FakeNamedPipeClient());
        Assert.False(status.IsConnected);
        Assert.Equal("Connecting…", status.Label);
    }

    [Fact]
    public void TracksPipeConnectAndDisconnect()
    {
        var pipe = new FakeNamedPipeClient();
        var status = new ServiceConnectionStatus(pipe);

        pipe.SimulateStatus(new StatusResponsePayload(MonitoringState.Stopped, DateTimeOffset.UtcNow));
        Assert.True(status.IsConnected);
        Assert.Equal("Connected", status.Label);

        pipe.SimulateDisconnect();
        Assert.False(status.IsConnected);
        Assert.Equal("Offline", status.Label);

        pipe.SimulateState(MonitoringState.Active);
        Assert.True(status.IsConnected);
    }
}
