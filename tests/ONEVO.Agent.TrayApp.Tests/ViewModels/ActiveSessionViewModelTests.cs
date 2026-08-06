using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ActiveSessionViewModelTests
{
    [Fact]
    public void Defaults_WorkingMode()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        Assert.False(vm.IsOnBreak);
        Assert.Equal("Working", vm.StatusText);
        Assert.False(vm.IsBreakConfirmVisible);
    }

    [Fact]
    public void RequestBreak_ShowsConfirmOverlay()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        vm.RequestBreakCommand.Execute(null);
        Assert.True(vm.IsBreakConfirmVisible);
    }

    [Fact]
    public void CancelBreakConfirm_HidesOverlay()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        vm.RequestBreakCommand.Execute(null);
        vm.CancelBreakConfirmCommand.Execute(null);
        Assert.False(vm.IsBreakConfirmVisible);
    }

    [Fact]
    public async Task ConfirmStartBreak_SendsStartBreakLifecycle()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new ActiveSessionViewModel(pipe);
        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);
        Assert.Contains(LifecycleAction.StartBreak, pipe.LifecycleActions);
        Assert.True(vm.IsOnBreak);
        Assert.Equal("On Break", vm.StatusText);
    }

    [Fact]
    public async Task EndBreak_SendsEndBreakLifecycle()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new ActiveSessionViewModel(pipe);
        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);
        await vm.EndBreakCommand.ExecuteAsync(null);
        Assert.Contains(LifecycleAction.EndBreak, pipe.LifecycleActions);
        Assert.False(vm.IsOnBreak);
    }

    [Fact]
    public async Task ClockOut_SendsClockOutLifecycle()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new ActiveSessionViewModel(pipe);
        await vm.ClockOutCommand.ExecuteAsync(null);
        Assert.Contains(LifecycleAction.ClockOut, pipe.LifecycleActions);
    }

    [Fact]
    public void ApplySession_SetsStartTimeAndSchedule()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        vm.ApplySession(new SessionSnapshot(
            clockIn, null, false, null,
            TimeSpan.Zero, TimeSpan.FromMinutes(5),
            "09:00 AM – 06:00 PM", 0));
        Assert.Equal("09:00 AM – 06:00 PM", vm.ScheduleDisplay);
        Assert.NotEqual("—", vm.StartTimeDisplay);
    }

    [Fact]
    public async Task LifecycleFailure_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, "INVALID_STATE", "Break is only available while working.",
                MonitoringState.Stopped, null)
        };
        var vm = new ActiveSessionViewModel(pipe);
        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);
        Assert.Equal("Break is only available while working.", vm.ErrorMessage);
    }
}
