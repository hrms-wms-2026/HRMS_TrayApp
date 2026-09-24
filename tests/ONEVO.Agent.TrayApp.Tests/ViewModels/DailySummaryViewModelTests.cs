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
}
