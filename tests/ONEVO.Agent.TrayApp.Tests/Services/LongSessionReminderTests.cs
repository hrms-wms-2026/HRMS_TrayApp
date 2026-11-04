namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class LongSessionReminderTests : IDisposable
{
    private static readonly DateTimeOffset ClockIn = new(2026, 11, 2, 3, 30, 0, TimeSpan.Zero);

    private readonly FakeNamedPipeClient _pipe = new();
    private readonly RecordingToast _toast = new();
    private DateTimeOffset _now = ClockIn;
    private readonly LongSessionReminder _reminder;

    public LongSessionReminderTests()
    {
        _pipe.LastKnownPolicy = new AgentPolicy { Version = "v1", TrayClockInEnabled = true };
        _reminder = new LongSessionReminder(_pipe, _toast, () => _now, startTimer: false);
    }

    public void Dispose() => _reminder.Dispose();

    private void SendStatus(MonitoringState state, DateTimeOffset? clockInAt = null)
        => _pipe.SimulateStatus(new StatusResponsePayload(
            state,
            _now,
            new SessionSnapshot(clockInAt ?? ClockIn, null, state == MonitoringState.Paused, null,
                TimeSpan.Zero, TimeSpan.Zero, null, 0)));

    [Fact]
    public void JustUnder24Hours_StaysSilent()
    {
        _now = ClockIn + TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1);
        SendStatus(MonitoringState.Active);

        Assert.Empty(_toast.Shown);
    }

    [Fact]
    public void At24Hours_AsksWhatToDo()
    {
        SendStatus(MonitoringState.Active);
        _now = ClockIn + TimeSpan.FromHours(24);
        _reminder.Check();

        var shown = Assert.Single(_toast.Shown);
        Assert.Equal("Still clocked in?", shown.Title);
        Assert.Equal("Clock out", shown.Button);
        Assert.Equal(LongSessionActivation.ClockOut, shown.Action);
    }

    [Fact]
    public void StatusArrivingAlreadyPast24Hours_AsksImmediately()
    {
        // Tray restarted the next day with the session still open.
        _now = ClockIn + TimeSpan.FromHours(30);
        SendStatus(MonitoringState.Active);

        Assert.Single(_toast.Shown);
    }

    [Fact]
    public void SameClockIn_AsksOnlyOnce()
    {
        _now = ClockIn + TimeSpan.FromHours(25);
        SendStatus(MonitoringState.Active);
        _reminder.Check();
        _now += TimeSpan.FromHours(3);
        SendStatus(MonitoringState.Active);

        Assert.Single(_toast.Shown);
    }

    [Fact]
    public void NewClockIn_RearmsTheReminder()
    {
        _now = ClockIn + TimeSpan.FromHours(25);
        SendStatus(MonitoringState.Active);

        var next = ClockIn + TimeSpan.FromHours(26);
        SendStatus(MonitoringState.Active, next);
        Assert.Single(_toast.Shown);

        _now = next + TimeSpan.FromHours(24);
        _reminder.Check();
        Assert.Equal(2, _toast.Shown.Count);
    }

    [Fact]
    public void OpenBreak_StillCounts()
    {
        _now = ClockIn + TimeSpan.FromHours(24);
        SendStatus(MonitoringState.Paused);

        Assert.Single(_toast.Shown);
    }

    [Theory]
    [InlineData(MonitoringState.Stopped)]
    [InlineData(MonitoringState.Locked)]
    [InlineData(MonitoringState.Unenrolled)]
    public void NoOpenSession_StaysSilent(MonitoringState state)
    {
        _now = ClockIn + TimeSpan.FromHours(30);
        SendStatus(state);

        Assert.Empty(_toast.Shown);
    }

    [Fact]
    public void ClockedOutBeforeTheMark_StaysSilent()
    {
        SendStatus(MonitoringState.Active);
        SendStatus(MonitoringState.Stopped);
        _now = ClockIn + TimeSpan.FromHours(24);
        _reminder.Check();

        Assert.Empty(_toast.Shown);
    }

    [Fact]
    public void ServiceDisconnected_StaysSilent()
    {
        SendStatus(MonitoringState.Active);
        _pipe.SimulateDisconnect();
        _now = ClockIn + TimeSpan.FromHours(24);
        _reminder.Check();

        Assert.Empty(_toast.Shown);
    }

    [Fact]
    public void TrayClockOutOff_PointsToThePortal()
    {
        _pipe.LastKnownPolicy = new AgentPolicy { Version = "v1", TrayClockInEnabled = false };
        _now = ClockIn + TimeSpan.FromHours(24);
        SendStatus(MonitoringState.Active);

        var shown = Assert.Single(_toast.Shown);
        Assert.Equal("Open portal", shown.Button);
        Assert.Equal(LongSessionActivation.OpenPortal, shown.Action);
    }

    [Fact]
    public void ClockOutRequest_IsConsumedOnce()
    {
        var raised = 0;
        _reminder.ClockOutRequested += () => raised++;

        Assert.False(_reminder.TryConsumeClockOutRequest());
        _reminder.RequestClockOut();

        Assert.Equal(1, raised);
        Assert.True(_reminder.TryConsumeClockOutRequest());
        Assert.False(_reminder.TryConsumeClockOutRequest());
    }

    private sealed class RecordingToast : ILongSessionToast
    {
        public List<(string Title, string Message, string Button, string Action)> Shown { get; } = [];

        public void ShowLongSessionReminder(string title, string message, string buttonText, string action)
            => Shown.Add((title, message, buttonText, action));
    }
}

public sealed class LongSessionActivationTests
{
    [Theory]
    [InlineData("kind=longsession;action=clockout", LongSessionActivation.ClockOut)]
    [InlineData("action=portal;kind=longsession", LongSessionActivation.OpenPortal)]
    [InlineData("kind=longsession;action=delete", null)]
    [InlineData("kind=longsession", null)]
    [InlineData("kind=generic;delivery=1;action=clockout", null)]
    [InlineData("attempt=00000000-0000-0000-0000-000000000001;decision=allow", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryGetAction_OnlyKnownLongSessionActions(string? args, string? expected)
        => Assert.Equal(expected, LongSessionActivation.TryGetAction(args));

    [Fact]
    public void IsLongSession_DoesNotClaimOtherToasts()
    {
        Assert.True(LongSessionActivation.IsLongSession("kind=longsession;action=clockout"));
        Assert.False(LongSessionActivation.IsLongSession("kind=generic;delivery=1;action=open_related"));
        Assert.False(LongSessionActivation.IsLongSession("kind=longsessionx;action=clockout"));
    }
}
