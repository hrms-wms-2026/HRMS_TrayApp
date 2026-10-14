using ONEVO.Agent.Shared.Models;
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
        // Productive starts as work when no idle samples.
        Assert.Equal("08:10:00", vm.ProductiveTimeDisplay);
    }

    [Fact]
    public void LoadFromSnapshot_UsesSnapshotIdle_NotDayMetrics()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        metrics.AddIdleSample(TimeSpan.FromMinutes(20));
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(45));
        metrics.AddAppUsageSample("Code.exe", TimeSpan.FromMinutes(90));

        var pipe = new FakeNamedPipeClient();
        var vm = new EndSessionViewModel(pipe, metrics);
        var clockIn  = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var clockOut = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockOut, false, null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(2) + TimeSpan.FromMinutes(30),
            null, 1,
            AccumulatedIdle: TimeSpan.FromMinutes(20)));

        Assert.Equal("00:20:00", vm.IdleTimeDisplay);
        Assert.Equal("02:30:00", vm.ProductiveTimeDisplay);
        Assert.Contains(vm.TopApps, a => a.Name.Contains("Code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromSnapshot_WorkDurationIncludesIdle_ProductiveExcludesIt()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var clockIn  = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var clockOut = new DateTimeOffset(2026, 8, 6, 16, 30, 0, TimeSpan.Zero);
        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockOut, false, null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(6),
            null, 1,
            AccumulatedIdle: TimeSpan.FromMinutes(30)));

        Assert.Equal("07:30:00", vm.TotalShiftDisplay);
        Assert.Equal("06:30:00", vm.WorkDurationDisplay);
        Assert.Equal("06:00:00", vm.ProductiveTimeDisplay);
        Assert.Equal("01:00:00", vm.BreakTimeDisplay);
    }

    [Fact]
    public void SyncCard_ShowsSyncedOnlyWhenNothingPending()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var now = DateTimeOffset.UtcNow;

        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, now,
            Buffer: new BufferStatusPayload("Ok", 12, 0, 1000)));
        Assert.Equal("Waiting to sync", vm.SyncTitle);
        Assert.Contains("12", vm.SyncCaption);
        Assert.Equal(string.Empty, vm.SyncTimeDisplay);

        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, now,
            Buffer: new BufferStatusPayload("Ok", 0, 0, 1000)));
        Assert.Equal("Synced to OneXso Cloud", vm.SyncTitle);
        Assert.NotEqual(string.Empty, vm.SyncTimeDisplay);

        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, now));
        Assert.Equal("Sync status unavailable", vm.SyncTitle);
    }

    [Fact]
    public void SavedCard_ReflectsLocalStorageHealth()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var now = DateTimeOffset.UtcNow;

        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, now,
            Buffer: new BufferStatusPayload("Ok", 3, 0, 1000)));
        Assert.Equal("Your work data has been securely saved.", vm.SavedTitle);
        Assert.NotEqual(string.Empty, vm.SavedAtDisplay);

        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, now,
            Buffer: new BufferStatusPayload("StorageError", 3, 0, 1000)));
        Assert.Equal("Your work data could not be saved", vm.SavedTitle);
        Assert.Equal(string.Empty, vm.SavedAtDisplay);
    }

    [Fact]
    public void SavedCard_OlderServiceWithoutBufferStatus_DoesNotClaimSaved()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        vm.ApplySyncStatus(new StatusResponsePayload(MonitoringState.Stopped, DateTimeOffset.UtcNow));

        Assert.Equal("Save status unavailable", vm.SavedTitle);
        Assert.Equal(string.Empty, vm.SavedAtDisplay);
    }

    [Theory]
    [InlineData(0, "Great job")]
    [InlineData(90, "Nice work")]
    [InlineData(240, "Thanks")]
    public void Greeting_FollowsActiveShare(int idleMinutes, string expectedStart)
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var clockIn = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        vm.LoadFromSnapshot(new SessionSnapshot(
            clockIn, clockIn.AddHours(9), false, null,
            TimeSpan.Zero, TimeSpan.FromHours(4), null, 0,
            AccumulatedIdle: TimeSpan.FromMinutes(idleMinutes)));

        Assert.StartsWith(expectedStart, vm.GreetingMessage);
    }

    [Fact]
    public void Footer_TracksPipeConnection()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new EndSessionViewModel(pipe);
        vm.OnAppearing();
        Assert.False(vm.Connection.IsConnected);

        pipe.SimulateStatus(new StatusResponsePayload(MonitoringState.Stopped, DateTimeOffset.UtcNow,
            Buffer: new BufferStatusPayload("Ok", 0, 0, 1000)));
        Assert.True(vm.Connection.IsConnected);
        Assert.Equal("Connected", vm.Connection.Label);

        pipe.SimulateDisconnect();
        Assert.False(vm.Connection.IsConnected);
        Assert.Equal("Offline", vm.Connection.Label);
        Assert.Equal("Cloud sync paused", vm.SyncTitle);
        vm.OnDisappearing();
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

    [Fact]
    public void CloseAppCommand_CanExecute()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        Assert.True(vm.CloseAppCommand.CanExecute(null));
        vm.CloseAppCommand.Execute(null);
    }

    [Fact]
    public async Task ViewSummaryCommand_CanExecute()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        Assert.True(vm.ViewSummaryCommand.CanExecute(null));
        await vm.ViewSummaryCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task DownloadSummaryCommand_WritesPdfFile()
    {
        var vm = new EndSessionViewModel(new FakeNamedPipeClient());
        var clockIn  = DateTimeOffset.Parse("2026-08-06T09:00:00+05:30");
        var clockOut = DateTimeOffset.Parse("2026-08-06T18:00:00+05:30");
        vm.LoadSummary(clockIn, clockOut, TimeSpan.FromMinutes(30), TimeSpan.Zero, TimeSpan.Zero);

        await vm.DownloadSummaryCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(vm.Message);
        var path = vm.Message!["Summary saved to ".Length..];
        try
        {
            Assert.EndsWith(".pdf", path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
