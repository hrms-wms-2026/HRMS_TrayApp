using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.Models;
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

    [Fact]
    public void StartAutoPause_EndAutoPause_Idle_AccumulatesIntoIdle_NotBreak()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(10);
        var t3 = t2.AddHours(1);

        session.ClockIn(t0);
        Assert.True(session.StartAutoPause(PauseReason.Idle, t1));
        Assert.True(session.EndAutoPause(PauseReason.Idle, t2));
        session.ClockOut(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedBreak);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(90), snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_WhileIdle_ClosedIdleOnly_OpenIdleInWorkMath()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var now = t1.AddMinutes(5);

        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t1);

        var snap = session.Snapshot(now);
        Assert.True(snap.IsIdle);
        Assert.Equal(t1, snap.CurrentIdleStartedAt);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromHours(1), snap.AccumulatedWork);
    }

    [Fact]
    public void DuplicateStartAutoPause_DoesNotResetIdleStart()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(10);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(10);

        session.ClockIn(t0);
        Assert.True(session.StartAutoPause(PauseReason.Idle, t1));
        Assert.False(session.StartAutoPause(PauseReason.Idle, t2));
        Assert.True(session.EndAutoPause(PauseReason.Idle, t3));

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(15), snap.AccumulatedIdle);
    }

    [Fact]
    public void IdleDetection_WhileManualBreakOpen_IsNoOp()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(5);
        var t3 = t1.AddMinutes(20);

        session.ClockIn(t0);
        session.StartBreak(t1);
        session.ObserveInbound(t1);
        Assert.False(session.StartAutoPause(PauseReason.Idle, t2));
        Assert.False(session.EndAutoPause(PauseReason.Idle, t3));
        session.EndBreak(t3);
        session.ObserveInbound(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(20), snap.AccumulatedBreak);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(30), snap.AccumulatedWork);
    }

    [Fact]
    public void StartBreak_WhileIdleOpen_ClosesIdleThenOpensBreak()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var idleStart = t0.AddMinutes(20);
        var breakStart = t0.AddMinutes(30);
        var breakEnd = t0.AddMinutes(40);

        session.ClockIn(t0);
        session.ObserveInbound(t0);
        session.StartAutoPause(PauseReason.Idle, idleStart);
        session.StartBreak(breakStart);
        session.EndBreak(breakEnd);
        session.ObserveInbound(breakEnd);

        var snap = session.Snapshot(breakEnd);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedBreak);
        Assert.False(snap.IsIdle);
        Assert.False(snap.IsOnBreak);
        Assert.Equal(TimeSpan.FromMinutes(20), snap.AccumulatedWork);
    }

    [Fact]
    public void ClockOut_WhileIdleOpen_FinalizesIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(2);
        var t2 = t1.AddMinutes(15);

        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t1);
        session.ClockOut(t2);

        var snap = session.Snapshot(t2);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(15), snap.AccumulatedIdle);
        Assert.Equal(t2, snap.ClockOutAt);
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedWork);
    }

    [Fact]
    public void ExistingBreakMath_UnchangedWhenNoIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(30);
        var t2 = t1.AddMinutes(10);
        var t3 = t2.AddHours(1);

        session.ClockIn(t0);
        session.StartBreak(t1);
        session.EndBreak(t2);
        session.ClockOut(t3);

        var snap = session.Snapshot(t3);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedBreak);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(90), snap.AccumulatedWork);
        Assert.Equal(1, snap.BreakSessionCount);
    }

    [Fact]
    public void ApplyDeviceStateIdle_True_BackDatesStartFromIdleSeconds()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var captured = t0.AddMinutes(5);
        session.ClockIn(t0);

        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = captured,
            IdleSeconds = 180,
            IsIdle = true
        }));

        var snap = session.Snapshot(captured);
        Assert.True(snap.IsIdle);
        Assert.Equal(captured - TimeSpan.FromSeconds(180), snap.CurrentIdleStartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), snap.AccumulatedWork);
    }

    [Fact]
    public void ApplyDeviceStateIdle_Sequence_CrossingThresholdBothDirections()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);

        var tActive = t0.AddMinutes(1);
        Assert.False(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tActive, IdleSeconds = 10, IsIdle = false
        }));
        session.ObserveInbound(tActive);

        var tIdle = t0.AddMinutes(4);
        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tIdle, IdleSeconds = 150, IsIdle = true
        }));
        session.ObserveInbound(tIdle);

        var tStillIdle = t0.AddMinutes(5);
        Assert.False(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tStillIdle, IdleSeconds = 210, IsIdle = true
        }));

        var tResume = t0.AddMinutes(6);
        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = tResume, IdleSeconds = 2, IsIdle = false
        }));
        session.ObserveInbound(tResume);

        var snap = session.Snapshot(tResume);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(4.5), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(1.5), snap.AccumulatedWork);
    }

    [Fact]
    public void ApplyDeviceStateIdle_AfterSleepClose_DoesNotDoubleCountViaIdleSeconds()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var sleepStart = t0.AddHours(1);
        var sleepEnd = t0.AddHours(3);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, sleepStart);
        session.EndAutoPause(PauseReason.Idle, sleepEnd);

        Assert.True(session.ApplyDeviceStateIdle(new DeviceStateSnapshotPayload
        {
            CapturedAt = sleepEnd,
            IdleSeconds = (int)TimeSpan.FromHours(2).TotalSeconds,
            IsIdle = true
        }));

        var snap = session.Snapshot(sleepEnd);
        Assert.True(snap.IsIdle);
        Assert.Equal(sleepEnd, snap.CurrentIdleStartedAt);
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromHours(1), snap.AccumulatedWork);
    }

    [Fact]
    public void ObserveInbound_GapOverThreshold_RetroactivelyAddsIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);

        var later = t0.AddMinutes(10);
        session.ObserveInbound(later);

        var snap = session.Snapshot(later);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(10), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
    }

    [Fact]
    public void ObserveInbound_GapUnderThreshold_DoesNotAddIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);
        session.ObserveInbound(t0.AddMinutes(2));

        var snap = session.Snapshot(t0.AddMinutes(2));
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(2), snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_GapFallback_WhenNoPauseOpen()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        var now = t0.AddMinutes(5);
        var snap = session.Snapshot(now);
        Assert.Equal(TimeSpan.FromMinutes(5), snap.AccumulatedIdle);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
    }

    [Fact]
    public void Snapshot_GapFallback_SkippedWhenIdleAlreadyOpen()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t0.AddMinutes(1));
        var now = t0.AddMinutes(10);
        var snap = session.Snapshot(now);
        Assert.True(snap.IsIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.FromMinutes(1), snap.AccumulatedWork);
    }

    [Fact]
    public void EndAutoPause_WrongReason_IsNoOp()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.StartAutoPause(PauseReason.Idle, t0.AddMinutes(1));
        Assert.False(session.EndAutoPause(PauseReason.ManualBreak, t0.AddMinutes(2)));
        var snap = session.Snapshot(t0.AddMinutes(2));
        Assert.True(snap.IsIdle);
    }

    [Fact]
    public void ClockOut_AfterObserveInboundGap_DoesNotDoubleCountIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var tOut = t0.AddHours(2);
        session.ClockIn(t0);
        session.ObserveInbound(tOut);
        session.ClockOut(tOut);

        var snap = session.Snapshot(tOut);
        Assert.Equal(TimeSpan.FromHours(2), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
        Assert.False(snap.IsIdle);
    }
}
