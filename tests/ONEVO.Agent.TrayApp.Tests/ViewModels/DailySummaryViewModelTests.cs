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
