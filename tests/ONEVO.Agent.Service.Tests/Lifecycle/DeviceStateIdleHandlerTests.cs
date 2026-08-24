using System.Text.Json;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Lifecycle;

public sealed class DeviceStateIdleHandlerTests
{
    private static CollectionRecord DeviceState(DateTimeOffset captured, int idleSeconds, bool isIdle) =>
        new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            RecordType = CollectionRecordTypes.DeviceStateSnapshot,
            SchemaVersion = CollectionSchemaVersions.DeviceStateSnapshotV1,
            CaptureTimestamp = captured,
            DeviceId = "test",
            Payload = JsonSerializer.SerializeToElement(new DeviceStateSnapshotPayload
            {
                CapturedAt = captured,
                IdleSeconds = idleSeconds,
                IsIdle = isIdle
            })
        };

    private static void ApplyRecords(PresenceSession session, params CollectionRecord[] records)
    {
        foreach (var record in records)
        {
            if (record.RecordType != CollectionRecordTypes.DeviceStateSnapshot)
                continue;
            var snap = record.Payload.Deserialize<DeviceStateSnapshotPayload>();
            if (snap is not null)
            {
                session.ApplyDeviceStateIdle(snap);
                session.ObserveInbound(snap.CapturedAt);
            }
        }
    }

    [Fact]
    public void HandlerLoop_IdleTrueThenFalse_SessionSnapshotCarriesAccumulatedIdle()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        session.ClockIn(t0);
        session.ObserveInbound(t0);

        var tIdle = t0.AddMinutes(3);
        var tResume = t0.AddMinutes(8);
        ApplyRecords(session,
            DeviceState(tIdle, idleSeconds: 180, isIdle: true),
            DeviceState(tResume, idleSeconds: 0, isIdle: false));

        var snap = session.Snapshot(tResume);
        Assert.False(snap.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(8), snap.AccumulatedIdle);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedWork);
        Assert.Equal(TimeSpan.Zero, snap.AccumulatedBreak);
    }
}
