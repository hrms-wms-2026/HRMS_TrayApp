using ONEVO.Agent.Service.Lifecycle;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

public sealed class PresenceSessionTests
{
    [Fact]
    public void ClockIn_StartBreak_EndBreak_ClockOut_AccumulatesCorrectly()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30); // start break
        var t2 = t1.AddMinutes(10); // end break
        var t3 = t2.AddHours(1);    // clock out

        session.ClockIn(t0);
        session.StartBreak(t1);
        session.EndBreak(t2);
        session.ClockOut(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(t0, snap.ClockInAt);
        Assert.Equal(t3, snap.ClockOutAt);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedBreak);
        Assert.Equal(1, snap.BreakSessionCount);
        // 1h40m wall - 10m break = 1h30m work
        Assert.Equal(TimeSpan.FromMinutes(90), snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_WhileOnBreak_ClosedBreakOnly_OpenBreakInWorkMath()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var now = t1.AddMinutes(5);

        session.ClockIn(t0);
        session.StartBreak(t1);

        var snap = session.Snapshot(now);
        Assert.True(snap.IsOnBreak);
        Assert.Equal(t1, snap.CurrentBreakStartedAt);
        // Closed break only — tray ticks open break from CurrentBreakStartedAt.
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedBreak);
        Assert.Equal(1, snap.BreakSessionCount);
        // Work = 1h05m wall - 5m open break = 1h
        Assert.Equal(TimeSpan.FromHours(1), snap.AccumulatedWork);
    }

    [Fact]
    public void ClockOut_WhileOnBreak_FinalizesBreak()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(2);
        var t2 = t1.AddMinutes(15);

        session.ClockIn(t0);
        session.StartBreak(t1);
        session.ClockOut(t2);

        var snap = session.Snapshot(t2);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromMinutes(15), snap.AccumulatedBreak);
        Assert.Equal(t2, snap.ClockOutAt);
    }
}
