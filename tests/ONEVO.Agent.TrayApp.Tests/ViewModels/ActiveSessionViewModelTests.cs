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
    public void ApplySession_OnBreak_PrimaryTimerUsesOpenBreak()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddMinutes(-10);
        var breakStart = DateTimeOffset.UtcNow.AddSeconds(-25);
        vm.ApplySession(new SessionSnapshot(
            ClockInAt: clockIn,
            ClockOutAt: null,
            IsOnBreak: true,
            CurrentBreakStartedAt: breakStart,
            AccumulatedBreak: TimeSpan.Zero,
            AccumulatedWork: TimeSpan.FromMinutes(9),
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: 1),
            isOnBreakOverride: true);

        Assert.True(vm.IsOnBreak);
        Assert.Equal("Break Timer", vm.PrimaryTimerLabel);
        // ~25s open break — allow small slack
        var parts = vm.PrimaryTimer.Split(':');
        Assert.Equal(3, parts.Length);
        var secs = int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
        Assert.InRange(secs, 20, 40);
    }

    [Fact]
    public void ApplySession_Working_PrimaryTimerIsWorkDuration()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddSeconds(-90);
        vm.ApplySession(new SessionSnapshot(
            clockIn, null, false, null,
            TimeSpan.Zero, TimeSpan.FromSeconds(90),
            "09:00 AM – 06:00 PM", 0));

        Assert.False(vm.IsOnBreak);
        Assert.Equal("Live Shift Timer", vm.PrimaryTimerLabel);
        var parts = vm.PrimaryTimer.Split(':');
        var secs = int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
        Assert.InRange(secs, 85, 100);
    }

    [Fact]
    public void UpdateTimersCore_TicksBreakWithoutServicePush()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var breakStart = DateTimeOffset.UtcNow.AddSeconds(-5);
        vm.ApplySession(new SessionSnapshot(
            clockIn, null, true, breakStart,
            TimeSpan.Zero, TimeSpan.FromMinutes(4),
            null, 1), true);

        var first = vm.PrimaryTimer;
        Thread.Sleep(1100);
        vm.UpdateTimersCore();
        var second = vm.PrimaryTimer;
        Assert.NotEqual(first, second);
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
