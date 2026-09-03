using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class AwaitingClockInViewModelTests
{
    [Fact]
    public void Constructor_SetsExpectedMessage()
    {
        var vm = new AwaitingClockInViewModel();

        Assert.Equal("Waiting for Clock In", vm.Title);
        Assert.Contains("web portal", vm.Message);
    }
}
