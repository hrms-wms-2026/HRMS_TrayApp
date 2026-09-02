namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

public class NamedPipeClientDevicePairingTests
{
    [Fact]
    public async Task SendDevicePairingStartAsync_RecordsEnvelope_ReturnsAutoSuccessByDefault()
    {
        var fake = new FakeNamedPipeClient();

        var result = await fake.SendDevicePairingStartAsync("Laptop", "Windows", "1.0.0", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Single(fake.SentEnvelopes, e => e.Type == IpcMessageTypes.DevicePairingStart);
    }

    [Fact]
    public async Task SendDevicePairingStartAsync_ReturnsCannedResult_WhenSet()
    {
        var fake = new FakeNamedPipeClient
        {
            NextDevicePairingStartedResult = new DevicePairingStartedPayload(false, "SERVICE_UNAVAILABLE")
        };

        var result = await fake.SendDevicePairingStartAsync("Laptop", "Windows", "1.0.0", CancellationToken.None);

        Assert.False(result!.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public void SimulateDevicePairingResult_InvokesOnDevicePairingResult()
    {
        var fake = new FakeNamedPipeClient();
        DevicePairingResultPayload? received = null;
        fake.OnDevicePairingResult += payload => received = payload;

        var pushed = new DevicePairingResultPayload { Success = true, EmployeeName = "Test Employee" };
        fake.SimulateDevicePairingResult(pushed);

        Assert.Equal(pushed, received);
    }
}
