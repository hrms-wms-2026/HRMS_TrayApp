using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class DailySummaryViewModelTests
{
    [Fact]
    public void LoadFromSnapshot_CopiesSessionMetrics()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new ONEVO.Agent.TrayApp.Services.SessionDayMetrics());
        var clockIn = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(5.5));
        var clockOut = new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.FromHours(5.5));

        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockOut, false, null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(8),
            "09:00 AM – 06:00 PM",
            2));

        Assert.Equal("08:00:00", vm.WorkingTimeDisplay);
        Assert.Equal("00:30:00", vm.BreakTimeDisplay);
        Assert.Equal("09:00:00", vm.TotalShiftDisplay);
        Assert.NotEqual("—", vm.ClockInDisplay);
        Assert.NotEqual("—", vm.ClockOutDisplay);
        Assert.Equal("8h 00m", vm.FocusCompactDisplay);
        Assert.Equal("8h 00m", vm.ActiveCompactDisplay);
        Assert.Equal("30m", vm.BreakCompactDisplay);
        Assert.Equal("2 breaks", vm.BreakSessionsCaption);
        Assert.True(vm.HasBreaks);
        Assert.Equal("Great Progress", vm.HighlightProgressTitle);
        Assert.Equal(0, vm.IdleShareFraction);
        Assert.Equal("↑ 100% tracked", vm.FocusTrendCaption);
        Assert.True(vm.IsFocusTrendPositive);
        Assert.Contains("highly focused", vm.InsightFocus, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.ActiveShareFraction > 0);
        Assert.Contains("focused", vm.InsightFocus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("regular breaks", vm.InsightBreaks, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.HasScreenshots);
        Assert.Empty(vm.Screenshots);
    }

    [Fact]
    public void OnAppearing_MapsAllowedScreenshotsIntoGallery()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var capturedAt = new DateTimeOffset(2026, 9, 18, 10, 5, 0, TimeSpan.FromHours(5.5));
        metrics.AddAllowedScreenshot(Guid.NewGuid(), capturedAt, new byte[] { 1, 2, 3, 4 });

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        var shot = Assert.Single(vm.Screenshots);
        Assert.True(vm.HasScreenshots);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, shot.JpegBytes);
        Assert.Equal(capturedAt.ToLocalTime().ToString("h:mm tt"), shot.TimeDisplay);
        Assert.Contains("1 screenshot", vm.ScreenshotsCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnAppearing_MapsSkippedAttemptAsRedNote()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var skippedAt = new DateTimeOffset(2026, 9, 18, 10, 7, 0, TimeSpan.FromHours(5.5));
        metrics.AddSkippedScreenshot(Guid.NewGuid(), skippedAt);

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        var note = Assert.Single(vm.Screenshots);
        Assert.True(vm.HasScreenshots);
        Assert.True(note.IsSkipped);
        Assert.Empty(note.JpegBytes);
        Assert.Equal("Screenshot skipped", note.NoteText);
        Assert.Equal(skippedAt.ToLocalTime().ToString("h:mm tt"), note.TimeDisplay);
        Assert.Contains("skipped", vm.ScreenshotsCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnAppearing_PopulatesFocusSparklinePointsFromHourlyBuckets()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var nineAm = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5.5));
        var tenAm = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(5.5));
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(45), nineAm);
        metrics.AddAppUsageSample("teams.exe", TimeSpan.FromMinutes(20), tenAm);

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        Assert.Equal(2, vm.FocusSparklinePoints.Count);
        Assert.Equal(1f, vm.FocusSparklinePoints[0]);
        Assert.Equal(20f / 45f, vm.FocusSparklinePoints[1], precision: 5);
    }

    [Fact]
    public async Task DownloadSummaryCommand_WritesPdfFile()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new ONEVO.Agent.TrayApp.Services.SessionDayMetrics());
        vm.LoadFromSnapshot(new SessionSnapshot(
            DateTimeOffset.Parse("2026-09-03T09:00:00+05:30"),
            DateTimeOffset.Parse("2026-09-03T18:00:00+05:30"),
            false, null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(8),
            null, 1));

        await vm.DownloadSummaryCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(vm.Message);
        var path = vm.Message!["Summary saved to ".Length..];
        try
        {
            Assert.EndsWith(".pdf", path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadTasksAsync_PopulatesTasksAndCaption()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextDailyTasksResult = new DailyTasksResultPayload(
                true, null, new DateOnly(2026, 10, 13), TotalMinutes: 95, TaskCount: 2, DoneCount: 1,
                [
                    new DailyTaskItemPayload(Guid.NewGuid(), "T-8", "Review PR", "Apollo", "In Progress", "#3B82F6",
                        40, 35, IsDone: false, IsClockedInNow: true),
                    new DailyTaskItemPayload(Guid.NewGuid(), "T-7", "Ship report", null, "Done", "#22C55E",
                        100, 60, IsDone: true, IsClockedInNow: false),
                ])
        };
        var vm = new DailySummaryViewModel(pipe, new FakeSessionDayMetrics());

        await vm.LoadTasksAsync(CancellationToken.None);

        Assert.True(vm.HasTasks);
        Assert.Equal("1 of 2 done · 1h 35m", vm.TasksCaption);
        Assert.Equal(2, vm.Tasks.Count);
        var running = vm.Tasks[0];
        Assert.Equal("Review PR", running.Title);
        Assert.Equal("T-8 · Apollo", running.Subtitle);
        Assert.Equal("In Progress", running.StatusName);
        Assert.Equal("35m", running.TimeDisplay);
        Assert.Equal(0.4, running.ProgressFraction, 3);
        Assert.Equal("40%", running.ProgressCaption);
        Assert.True(running.IsRunning);
        Assert.Equal("T-7", vm.Tasks[1].Subtitle);
        Assert.Equal("1h 00m", vm.Tasks[1].TimeDisplay);
        Assert.Contains(pipe.SentEnvelopes, e => e.Type == IpcMessageTypes.DailyTasksRequest);
    }

    [Fact]
    public async Task LoadTasksAsync_NoTasksToday_ShowsEmptyMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextDailyTasksResult = new DailyTasksResultPayload(true, null, new DateOnly(2026, 10, 13), 0, 0, 0, [])
        };
        var vm = new DailySummaryViewModel(pipe, new FakeSessionDayMetrics());

        await vm.LoadTasksAsync(CancellationToken.None);

        Assert.False(vm.HasTasks);
        Assert.Empty(vm.Tasks);
        Assert.Contains("No task clock-ins today", vm.TasksEmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadTasksAsync_ServiceUnavailable_FailsSoft()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextDailyTasksResult = new DailyTasksResultPayload(false, "SERVICE_UNAVAILABLE", null, 0, 0, 0, [])
        };
        var vm = new DailySummaryViewModel(pipe, new FakeSessionDayMetrics());

        await vm.LoadTasksAsync(CancellationToken.None);

        Assert.False(vm.HasTasks);
        Assert.False(vm.IsLoadingTasks);
        Assert.True(vm.CanRetryTasks);
        Assert.Contains("unavailable", vm.TasksEmptyMessage, StringComparison.OrdinalIgnoreCase);

        pipe.NextDailyTasksResult = new DailyTasksResultPayload(true, null, new DateOnly(2026, 10, 13), 30, 1, 0,
            [new DailyTaskItemPayload(Guid.NewGuid(), "T-1", "Retry worked", null, "Doing", "#3B82F6", 10, 30, false, true)]);
        await vm.RetryTasksCommand.ExecuteAsync(null);

        Assert.True(vm.HasTasks);
        Assert.False(vm.CanRetryTasks);
        Assert.Equal("Retry worked", vm.Tasks[0].Title);
    }

    [Fact]
    public async Task LoadTasksAsync_PipeTimeout_FailsSoft()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new FakeSessionDayMetrics());

        await vm.LoadTasksAsync(CancellationToken.None);

        Assert.False(vm.HasTasks);
        Assert.Contains("unavailable", vm.TasksEmptyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DailyTaskItem_ShowsTodaysPushRange_WhenPushedToday()
    {
        var pushed = DailyTaskItem.From(new DailyTaskItemPayload(
            Guid.NewGuid(), "T-1", "Login", null, "In Progress", "#3B82F6", 70, 90,
            IsDone: false, IsClockedInNow: false, TodayFromPercent: 40, TodayToPercent: 70));
        var notPushed = DailyTaskItem.From(new DailyTaskItemPayload(
            Guid.NewGuid(), "T-2", "Tests", null, "In Progress", "#3B82F6", 40, 30,
            IsDone: false, IsClockedInNow: true));

        Assert.Equal("40% → 70%", pushed.ProgressCaption);
        Assert.Equal(0.7, pushed.ProgressFraction);
        Assert.Equal("40%", notPushed.ProgressCaption);
    }

    [Fact]
    public void DailyTaskItem_InvalidStatusColor_FallsBackToNeutral()
    {
        var item = DailyTaskItem.From(new DailyTaskItemPayload(
            Guid.NewGuid(), "", "", null, null, "not-a-color", 150, -5, IsDone: false, IsClockedInNow: false));

        Assert.Equal("Untitled task", item.Title);
        Assert.Equal("In progress", item.StatusName);
        Assert.Equal(1.0, item.ProgressFraction);
        Assert.NotNull(item.StatusSwatch);
    }

    [Fact]
    public void DayVerdict_DoesNotPraiseALowActivityDay()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new ONEVO.Agent.TrayApp.Services.SessionDayMetrics());
        var clockIn = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockIn.AddHours(8), false, null,
            TimeSpan.Zero, TimeSpan.FromHours(2), null, 0,
            AccumulatedIdle: TimeSpan.FromHours(6)));

        Assert.DoesNotContain("Excellent", vm.ExcellentDayCaption);
        Assert.DoesNotContain("excellent", vm.Headline);
    }

    [Fact]
    public void DailyGoal_IsScheduleSpanLessBreakAllowance_AndNullWithoutSchedule()
    {
        Assert.Equal(TimeSpan.FromHours(8),
            DailySummaryViewModel.DailyGoal(new TimeOnly(9, 0), new TimeOnly(18, 0), breakAllowanceMinutes: 60));
        Assert.Equal(TimeSpan.FromHours(9),
            DailySummaryViewModel.DailyGoal(new TimeOnly(9, 0), new TimeOnly(18, 0), breakAllowanceMinutes: null));
        Assert.Equal(TimeSpan.FromHours(8),
            DailySummaryViewModel.DailyGoal(new TimeOnly(22, 0), new TimeOnly(6, 0), null));
        Assert.Null(DailySummaryViewModel.DailyGoal(null, new TimeOnly(18, 0), 60));
    }

    [Fact]
    public void LoadFromSnapshot_WithPolicySchedule_ShowsGoalProgress()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new ONEVO.Agent.Shared.Models.AgentPolicy
            {
                Version = "1",
                ScheduleStart = new TimeOnly(9, 0),
                ScheduleEnd = new TimeOnly(18, 0),
            }
        };
        var vm = new DailySummaryViewModel(pipe, new FakeSessionDayMetrics());
        var clockIn = DateTimeOffset.Now.Date.AddHours(9);

        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockIn.AddHours(9), false, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(7.5), null, 1,
            BreakAllowanceMinutes: 60));

        Assert.True(vm.HasDailyGoal);
        Assert.Equal("94%", vm.GoalPercentCaption);
        Assert.Equal("/ 8h 00m", vm.GoalTargetDisplay);
        Assert.Equal(7.5 / 8, vm.GoalFraction, 3);
    }

    [Fact]
    public void LoadFromSnapshot_WithoutSchedule_HasNoGoal()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new FakeSessionDayMetrics());
        var clockIn = DateTimeOffset.Now.Date.AddHours(9);

        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockIn.AddHours(8), false, null, TimeSpan.Zero, TimeSpan.FromHours(8), null, 0));

        Assert.False(vm.HasDailyGoal);
        Assert.Equal("—", vm.GoalPercentCaption);
    }

    [Fact]
    public void BuildTimeline_PlacesBreaksAndIdleOverActiveSpan_OnWholeHourRange()
    {
        var clockIn = new DateTimeOffset(new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Local));
        var clockOut = clockIn.AddHours(8.5);
        var breakStart = clockIn.AddHours(2);

        var (segments, ticks) = DailySummaryViewModel.BuildTimeline(clockIn, clockOut,
        [
            new SessionPauseInterval(SessionPauseInterval.Break, breakStart, breakStart.AddHours(1)),
            new SessionPauseInterval(SessionPauseInterval.Idle, clockIn.AddHours(5), clockIn.AddHours(5.5)),
        ]);

        Assert.Equal(3, segments.Count);
        Assert.Equal(DailySummaryViewModel.ActiveColor, segments[0].ColorHex);
        Assert.Equal(DailySummaryViewModel.BreakColor, segments[1].ColorHex);
        Assert.Equal(DailySummaryViewModel.IdleColor, segments[2].ColorHex);
        // The range is rounded out to whole hours around clock-in/out, so the active span sits
        // inside it, and the break's width is its share of that range.
        Assert.True(segments[0].Start > 0 && segments[0].End <= 1);
        var rangeHours = (ticks.Count - 1) * 1.0;
        Assert.Equal(1.0 / rangeHours, segments[1].End - segments[1].Start, 3);
        Assert.Equal(0, ticks[0].Position);
        Assert.Equal(1, ticks[^1].Position);
        Assert.EndsWith(":00", ticks[0].Label);
    }

    [Fact]
    public void BuildTimeline_UnknownPauses_DrawsOnlyTheActiveSpan()
    {
        var clockIn = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

        var (segments, _) = DailySummaryViewModel.BuildTimeline(clockIn, clockIn.AddHours(8), null);

        Assert.Single(segments);
    }

    [Fact]
    public void OnAppearing_AppUsage_SharesAreOfAllApps_WithOthersRow()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var at = DateTimeOffset.Now;
        metrics.AddAppUsageSample("code", TimeSpan.FromMinutes(50), at);
        metrics.AddAppUsageSample("chrome", TimeSpan.FromMinutes(20), at);
        metrics.AddAppUsageSample("teams", TimeSpan.FromMinutes(10), at);
        metrics.AddAppUsageSample("figma", TimeSpan.FromMinutes(8), at);
        metrics.AddAppUsageSample("outlook", TimeSpan.FromMinutes(7), at);
        metrics.AddAppUsageSample("notepad", TimeSpan.FromMinutes(3), at);
        metrics.AddAppUsageSample("calc", TimeSpan.FromMinutes(2), at);

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        Assert.True(vm.HasAppUsage);
        Assert.Equal(6, vm.AppUsageRows.Count);
        Assert.Equal("Visual Studio Code", vm.AppUsageRows[0].Name);
        Assert.Equal("50%", vm.AppUsageRows[0].Percent);
        Assert.Equal(1.0, vm.AppUsageRows[0].BarFraction, 3);
        var others = vm.AppUsageRows[^1];
        Assert.Equal("Others", others.Name);
        Assert.Equal("5%", others.Percent);
        Assert.Equal("5m", others.DurationText);
    }

    [Fact]
    public void OnAppearing_ScreenshotTiles_KeepsLatestSix()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var t0 = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 8; i++)
            metrics.AddAllowedScreenshot(Guid.NewGuid(), t0.AddMinutes(i), new byte[] { (byte)i });

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        Assert.Equal(8, vm.Screenshots.Count);
        Assert.Equal(6, vm.ScreenshotTiles.Count);
        Assert.Equal(new byte[] { 2 }, vm.ScreenshotTiles[0].JpegBytes);
    }

    [Fact]
    public async Task LoadTasksAsync_FillsCompletedTasksCard_AndScheduleOrderWithTimeRanges()
    {
        var first = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        var pipe = new FakeNamedPipeClient
        {
            NextDailyTasksResult = new DailyTasksResultPayload(
                true, null, new DateOnly(2026, 10, 13), TotalMinutes: 240, TaskCount: 4, DoneCount: 3,
                [
                    new DailyTaskItemPayload(Guid.NewGuid(), "T-3", "Bug Fixes", null, "In Progress", "#3B82F6",
                        50, 120, IsDone: false, IsClockedInNow: false,
                        FirstClockInAtUtc: first.AddHours(4), LastActivityAtUtc: first.AddHours(6)),
                    new DailyTaskItemPayload(Guid.NewGuid(), "T-1", "UI Review", null, "Done", "#22C55E",
                        100, 60, IsDone: true, IsClockedInNow: false,
                        FirstClockInAtUtc: first, LastActivityAtUtc: first.AddHours(1)),
                ])
        };
        var vm = new DailySummaryViewModel(pipe, new FakeSessionDayMetrics());

        await vm.LoadTasksAsync(CancellationToken.None);

        Assert.Equal("3 tasks", vm.TasksDoneDisplay);
        Assert.Equal("75%", vm.TasksPercentCaption);
        Assert.Equal(3, vm.TasksDoneCount);
        Assert.Equal(1, vm.TasksPendingCount);
        Assert.Equal(4, vm.TasksTotalCount);
        Assert.Equal("UI Review", vm.Tasks[0].Title);
        Assert.Equal($"{first.ToLocalTime():HH:mm} - {first.AddHours(1).ToLocalTime():HH:mm}", vm.Tasks[0].TimeRange);
        Assert.Contains("completed 3 tasks", vm.BannerMessage);
    }

    private static SessionSnapshot MondaySession() => new(
        new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(5.5)),
        new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.FromHours(5.5)),
        false, null, TimeSpan.FromMinutes(30), TimeSpan.FromHours(8), null, 1);

    [Fact]
    public void ToPdfData_ListsTheSameAppRowsAsThePage_IncludingOthers()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var minutes = new[] { 60, 50, 40, 30, 20, 10 };
        for (var i = 0; i < minutes.Length; i++)
            metrics.AddAppUsageSample($"app{i}.exe", TimeSpan.FromMinutes(minutes[i]));
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.LoadFromSnapshot(MondaySession());

        var pdf = vm.ToPdfData();

        Assert.Equal(vm.AppUsageRows.Select(r => r.Name), pdf.TopApps.Select(a => a.Name));
        Assert.Equal(vm.AppUsageRows.Select(r => r.DurationText), pdf.TopApps.Select(a => a.Duration));
        Assert.Equal(6, pdf.TopApps.Count);
        Assert.Equal("Others", pdf.TopApps[^1].Name);
    }

    [Fact]
    public void ToPdfData_IsDatedWithTheSessionDay()
    {
        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), new ONEVO.Agent.TrayApp.Services.SessionDayMetrics());
        var session = MondaySession();
        vm.LoadFromSnapshot(session);

        Assert.Equal(session.ClockOutAt!.Value.ToLocalTime().Date, vm.ToPdfData().SessionDay);
        Assert.Equal(session.ClockOutAt.Value.ToLocalTime().ToString("MMMM d, yyyy"), vm.DateDisplay);
    }
}
