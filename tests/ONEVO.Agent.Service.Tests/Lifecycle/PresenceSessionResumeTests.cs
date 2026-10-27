using ONEVO.Agent.Service.Lifecycle;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

/// <summary>
/// AUD-001: after a Service restart mid-day the open session is rebuilt from the backend's
/// clock-in time. That rebuild must not book the whole elapsed day as idle (the 180 s gap fallback
/// measured from the hours-old clock-in) and must keep the day's completed breaks.
/// </summary>
public sealed class PresenceSessionResumeTests
{
    private static readonly DateTimeOffset ClockIn = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Restart = ClockIn.AddHours(4);

    [Fact]
    public void ResumeOpenSession_DoesNotCountTimeBeforeTheRestartAsIdle()
    {
        var session = new PresenceSession();

        session.ResumeOpenSession(ClockIn, TimeSpan.FromMinutes(30), observedAt: Restart);
        var snap = session.Snapshot(Restart);

        Assert.Equal(ClockIn, snap.ClockInAt);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(30), snap.AccumulatedBreak);
        Assert.Equal(TimeSpan.FromHours(3.5), snap.AccumulatedWork);
        Assert.Equal(1, snap.BreakSessionCount);
    }

    [Fact]
    public void ResumeOpenSession_GapFallbackStillAppliesAfterTheRestart()
    {
        var session = new PresenceSession();

        session.ResumeOpenSession(ClockIn, TimeSpan.Zero, observedAt: Restart);
        var snap = session.Snapshot(Restart.AddMinutes(10)); // no tray activity for 10 min after restart

        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromHours(4), snap.AccumulatedWork);
    }

    [Fact]
    public void ResumeOpenSession_IdleSnapshotCannotBackdateBeforeTheRestart()
    {
        var session = new PresenceSession();
        session.ResumeOpenSession(ClockIn, TimeSpan.Zero, observedAt: Restart);

        // A device-state idle report claiming 2 h idle right after the restart starts at the restart.
        session.ApplyDeviceStateIdle(new ONEVO.Agent.Shared.Models.DeviceStateSnapshotPayload
        {
            CapturedAt = Restart.AddMinutes(1),
            IsIdle = true,
            IdleSeconds = 7200
        });
        var snap = session.Snapshot(Restart.AddMinutes(1));

        Assert.Equal(Restart, snap.CurrentIdleStartedAt);
    }
}
