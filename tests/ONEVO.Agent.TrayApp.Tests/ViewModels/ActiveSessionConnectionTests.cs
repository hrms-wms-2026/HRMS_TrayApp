using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ActiveSessionConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnect_ReplacesLiveClaimsAndDismissesPendingActions(bool onBreak)
    {
        var pipe = new FakeNamedPipeClient();
        var connection = new ServiceConnectionStatus(pipe);
        await using var vm = Create(pipe, connection);
        vm.OnAppearing();
        pipe.SimulateStatus(Status(onBreak));
        if (onBreak)
            vm.RequestEndBreakCommand.Execute(null);
        else
            vm.RequestBreakCommand.Execute(null);

        pipe.SimulateDisconnect();

        Assert.Equal("Offline", vm.StatusText);
        Assert.Equal("Reconnecting", vm.HeaderAccent);
        Assert.DoesNotContain("monitoring is now active", vm.HintMessage);
        Assert.Contains("reconnect", vm.HintMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsBreakConfirmVisible);
        Assert.False(vm.IsEndBreakConfirmVisible);
        Assert.False(vm.IsClockOutConfirmVisible);
        Assert.False(vm.RequestBreakCommand.CanExecute(null));
        Assert.False(vm.RequestEndBreakCommand.CanExecute(null));
        Assert.False(vm.RequestClockOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task Disconnect_BlocksAlreadyQueuedLifecycleCommands()
    {
        var pipe = new FakeNamedPipeClient();
        var connection = new ServiceConnectionStatus(pipe);
        await using var vm = Create(pipe, connection);
        vm.OnAppearing();
        pipe.SimulateStatus(Status(false));
        vm.RequestClockOutCommand.Execute(null);

        pipe.SimulateDisconnect();
        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);
        await vm.EndBreakCommand.ExecuteAsync(null);
        await vm.ClockOutCommand.ExecuteAsync(null);

        Assert.Empty(pipe.LifecycleActions);
        Assert.False(vm.IsClockOutConfirmVisible);
        Assert.False(vm.ConfirmStartBreakCommand.CanExecute(null));
        Assert.False(vm.EndBreakCommand.CanExecute(null));
        Assert.False(vm.ClockOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task AppearingAfterDisconnect_DoesNotPresentCachedSessionAsLive()
    {
        var pipe = new FakeNamedPipeClient { LastKnownStatus = Status(false) };
        var connection = new ServiceConnectionStatus(pipe);
        pipe.SimulateDisconnect();
        await using var vm = Create(pipe, connection);

        vm.OnAppearing();

        Assert.Equal("Offline", vm.StatusText);
        Assert.NotEqual("—", vm.StartTimeDisplay);
        Assert.False(vm.RequestBreakCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false, "Working", "Clocked In")]
    [InlineData(true, "On Break", "On Break")]
    public async Task Reconnect_RestoresAuthoritativeSessionAndActions(bool onBreak, string status, string heading)
    {
        var pipe = new FakeNamedPipeClient();
        var connection = new ServiceConnectionStatus(pipe);
        await using var vm = Create(pipe, connection);
        vm.OnAppearing();
        pipe.SimulateStatus(Status(!onBreak));
        pipe.SimulateDisconnect();
        Assert.Equal("Offline", vm.StatusText);

        pipe.SimulateStatus(Status(onBreak));

        Assert.Equal(status, vm.StatusText);
        Assert.Equal(heading, vm.HeaderAccent);
        Assert.True(vm.RequestBreakCommand.CanExecute(null));
        Assert.True(vm.RequestEndBreakCommand.CanExecute(null));
        Assert.True(vm.RequestClockOutCommand.CanExecute(null));
        Assert.Equal(onBreak, vm.IsOnBreak);
    }

    private static ActiveSessionViewModel Create(FakeNamedPipeClient pipe, ServiceConnectionStatus connection) =>
        new(pipe, new SessionDayMetrics(), NoOpCollectorLifecycleCoordinator.Instance,
            NoOpLocationService.Instance, connection: connection);

    private static StatusResponsePayload Status(bool onBreak) =>
        new(onBreak ? MonitoringState.Paused : MonitoringState.Active, DateTimeOffset.UtcNow,
            new SessionSnapshot(DateTimeOffset.UtcNow.AddHours(-1), null, onBreak,
                onBreak ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
                TimeSpan.Zero, TimeSpan.FromMinutes(55), "09:00 AM – 06:00 PM", 0));
}
