using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ClockInViewModelTests
{
    private static ClockInViewModel Make() =>
        new(new FakeNamedPipeClient());

    [Fact]
    public void LiveTimer_DefaultsToZero()
    {
        var vm = Make();
        Assert.Equal("00:00:00", vm.LiveTimer);
    }

    [Fact]
    public void ConnectionStatus_DefaultsOnline()
    {
        var vm = Make();
        Assert.Equal("Online", vm.ConnectionStatus);
    }

    [Fact]
    public void InternetStatus_DefaultsExcellentConnection()
    {
        var vm = Make();
        Assert.Equal("Excellent Connection", vm.InternetStatus);
    }

    [Fact]
    public void DeviceType_DefaultsWindowsDesktop()
    {
        var vm = Make();
        Assert.Equal("Windows Desktop", vm.DeviceType);
    }

    [Fact]
    public void Greeting_IsNotEmpty()
    {
        var vm = Make();
        Assert.NotEmpty(vm.Greeting);
    }

    [Fact]
    public async Task ClockInCommand_SendsEnvelopeViaPipe()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ClockInViewModel(pipe);
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.Single(pipe.SentEnvelopes);
    }

    [Fact]
    public void ClockInCommand_AlwaysEnabled()
    {
        var vm = Make();
        Assert.True(vm.ClockInCommand.CanExecute(null));
    }
}
