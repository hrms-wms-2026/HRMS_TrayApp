using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
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
        Assert.Equal("", vm.HintMessage);
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
        Assert.Equal("Break started. Enjoy your break! ☕", vm.HintMessage);
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
    public void ApplySession_IdleOpen_WorkDurationExcludesIdle()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddMinutes(-10);
        var idleStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        vm.ApplySession(new SessionSnapshot(
            ClockInAt: clockIn,
            ClockOutAt: null,
            IsOnBreak: false,
            CurrentBreakStartedAt: null,
            AccumulatedBreak: TimeSpan.Zero,
            AccumulatedWork: TimeSpan.FromMinutes(8),
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: 0,
            AccumulatedIdle: TimeSpan.Zero,
            IsIdle: true,
            CurrentIdleStartedAt: idleStart));

        var workParts = vm.WorkDurationDisplay.Split(':');
        var workSecs = int.Parse(workParts[0]) * 3600 + int.Parse(workParts[1]) * 60 + int.Parse(workParts[2]);
        Assert.InRange(workSecs, 7 * 60 - 5, 8 * 60 + 5);

        var idleParts = vm.IdleTimeDisplay.Split(':');
        var idleSecs = int.Parse(idleParts[0]) * 3600 + int.Parse(idleParts[1]) * 60 + int.Parse(idleParts[2]);
        Assert.InRange(idleSecs, 110, 130);
    }

    [Fact]
    public void ApplySession_ClosedIdle_ShowsIdleAndReducedWork()
    {
        var vm = new ActiveSessionViewModel(new FakeNamedPipeClient());
        var clockIn = DateTimeOffset.UtcNow.AddHours(-1);
        vm.ApplySession(new SessionSnapshot(
            clockIn, null, false, null,
            TimeSpan.Zero, TimeSpan.FromMinutes(50),
            "09:00 AM – 06:00 PM", 0,
            AccumulatedIdle: TimeSpan.FromMinutes(10),
            IsIdle: false,
            CurrentIdleStartedAt: null));

        Assert.Equal("00:10:00", vm.IdleTimeDisplay);
        var workParts = vm.WorkDurationDisplay.Split(':');
        var workSecs = int.Parse(workParts[0]) * 3600 + int.Parse(workParts[1]) * 60 + int.Parse(workParts[2]);
        Assert.InRange(workSecs, 49 * 60, 51 * 60);
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

    // --- Pre-stop collector drain (Task 6) ---
    //
    // OrderRecordingLifecycleCoordinator proves ordering, not mere co-occurrence: it snapshots
    // pipe.LifecycleActions.Count at the moment PrepareForPauseAsync runs. If that count is 0, no
    // lifecycle command had been sent yet — i.e. the drain genuinely happened first.

    [Fact]
    public async Task ConfirmStartBreak_CallsPrepareForPause_BeforeSendingStartBreak()
    {
        var pipe = new FakeNamedPipeClient();
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.LifecycleActionCountWhenPrepareCalled);
        Assert.Contains(LifecycleAction.StartBreak, pipe.LifecycleActions);
    }

    [Fact]
    public async Task ClockOut_CallsPrepareForPause_BeforeSendingClockOut()
    {
        var pipe = new FakeNamedPipeClient();
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.ClockOutCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.LifecycleActionCountWhenPrepareCalled);
        Assert.Contains(LifecycleAction.ClockOut, pipe.LifecycleActions);
    }

    [Fact]
    public async Task EndBreak_DoesNotCallPrepareForPause()
    {
        // EndBreak resumes monitoring rather than pausing it — it must not drain collectors.
        var pipe = new FakeNamedPipeClient();
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.EndBreakCommand.ExecuteAsync(null);

        Assert.False(coordinator.PrepareCalled);
    }

    [Fact]
    public async Task ConfirmStartBreak_RejectedWhileStateStillActive_CallsResume()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, "REJECTED", "Cannot start break right now.", MonitoringState.Active, null)
        };
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);

        Assert.True(coordinator.ResumeCalled);
    }

    [Fact]
    public async Task ConfirmStartBreak_RejectedWhileStateNotActive_DoesNotCallResume()
    {
        // Discriminates "the Service rejected it AND authoritative state is still Active" from a
        // rejection that already moved state elsewhere (e.g. stale session) — only the former
        // should reconcile collectors back on.
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, "NO_ACTIVE_SESSION", "not clocked in", MonitoringState.Stopped, null)
        };
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.ConfirmStartBreakCommand.ExecuteAsync(null);

        Assert.False(coordinator.ResumeCalled);
    }

    [Fact]
    public async Task ClockOut_Succeeds_DoesNotCallResume()
    {
        var pipe = new FakeNamedPipeClient();
        var coordinator = new OrderRecordingLifecycleCoordinator(pipe);
        var vm = new ActiveSessionViewModel(pipe, new SessionDayMetrics(), coordinator);

        await vm.ClockOutCommand.ExecuteAsync(null);

        Assert.False(coordinator.ResumeCalled);
    }

    /// <summary>
    /// Records whether/when it was called relative to <see cref="FakeNamedPipeClient.LifecycleActions"/>
    /// so ordering can be asserted, not just co-occurrence.
    /// </summary>
    private sealed class OrderRecordingLifecycleCoordinator(FakeNamedPipeClient pipe) : ICollectorLifecycleCoordinator
    {
        public bool PrepareCalled { get; private set; }
        public bool ResumeCalled { get; private set; }
        public int LifecycleActionCountWhenPrepareCalled { get; private set; } = -1;

        public Task PrepareForPauseAsync(CancellationToken ct)
        {
            PrepareCalled = true;
            LifecycleActionCountWhenPrepareCalled = pipe.LifecycleActions.Count;
            return Task.CompletedTask;
        }

        public Task ResumeAfterRejectedPauseAsync(CancellationToken ct)
        {
            ResumeCalled = true;
            return Task.CompletedTask;
        }
    }
}
