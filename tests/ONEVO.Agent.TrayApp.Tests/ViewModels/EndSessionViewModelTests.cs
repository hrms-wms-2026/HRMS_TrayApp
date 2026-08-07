using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class EndSessionViewModelTests
{
    [Fact]
    public void LoadFromSnapshot_FormatsDisplays()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var clockIn  = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.FromHours(5.5));
        var clockOut = new DateTimeOffset(2026, 8, 6, 18, 0, 0, TimeSpan.FromHours(5.5));

        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockOut, false, null,
            TimeSpan.FromMinutes(50),
            TimeSpan.FromHours(8) + TimeSpan.FromMinutes(10),
            "09:00 AM – 06:00 PM",
            3));

        Assert.Equal("Clocked Out", vm.StatusText);
        Assert.Equal("00:50:00", vm.BreakTimeDisplay);
        Assert.Equal("08:10:00", vm.WorkingTimeDisplay);
        Assert.Equal("3", vm.BreakSessionsDisplay);
        Assert.Equal("09:00:00", vm.TotalShiftDisplay);
        Assert.NotEqual("—", vm.ClockInDisplay);
        Assert.NotEqual("—", vm.ClockOutDisplay);
    }

    [Fact]
    public void LoadSummary_LegacyPath_StillWorks()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var clockIn  = DateTimeOffset.Parse("2026-08-06T09:00:00+05:30");
        var clockOut = DateTimeOffset.Parse("2026-08-06T18:00:00+05:30");
        vm.LoadSummary(clockIn, clockOut, TimeSpan.FromMinutes(30), TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal("00:30:00", vm.BreakTimeDisplay);
        Assert.NotEqual("—", vm.ClockInDisplay);
    }
}
