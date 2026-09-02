namespace ONEVO.Agent.Shared.Tests;

using System.Text.Json;
using ONEVO.Agent.Shared.IPC;
using Xunit;

public class IpcEnvelopeTests
{
    [Fact]
    public void Envelope_DefaultVersion_IsCurrentProtocol()
    {
        var envelope = new IpcEnvelope { Type = "StatusRequest" };
        Assert.Equal(IpcProtocolVersion.Current, envelope.Version);
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrip_PreservesTypeAndVersion()
    {
        var original = new IpcEnvelope
        {
            Type = "StatusRequest",
            CorrelationId = "test-cid-001",
            Timestamp = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<IpcEnvelope>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Type, restored.Type);
        Assert.Equal(original.CorrelationId, restored.CorrelationId);
        Assert.Equal(IpcProtocolVersion.Current, restored.Version);
    }

    [Fact]
    public void Serialize_UsesShortPropertyNames()
    {
        var envelope = new IpcEnvelope { Type = "Ping" };
        var json = JsonSerializer.Serialize(envelope);

        Assert.Contains("\"v\":", json);
        Assert.Contains("\"type\":", json);
        Assert.Contains("\"cid\":", json);
        Assert.Contains("\"ts\":", json);
    }

    [Fact]
    public void IsCompatible_CurrentVersion_ReturnsTrue()
    {
        Assert.True(IpcProtocolVersion.IsCompatible(IpcProtocolVersion.Current));
    }

    [Fact]
    public void IsCompatible_OldVersion_ReturnsFalse()
    {
        Assert.False(IpcProtocolVersion.IsCompatible("0.9"));
    }

    [Fact]
    public void IsCompatible_Null_ReturnsFalse()
    {
        Assert.False(IpcProtocolVersion.IsCompatible(null));
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<IpcEnvelope>("not-json"));
    }

    [Fact]
    public void LifecyclePayloads_RoundTrip()
    {
        var session = new SessionSnapshot(
            DateTimeOffset.UtcNow,
            null,
            false,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            "09:00 AM – 06:00 PM",
            0);

        var cmd = new LifecycleCommandPayload(LifecycleAction.ClockIn);
        var cmdJson = JsonSerializer.Serialize(cmd);
        var cmdRestored = JsonSerializer.Deserialize<LifecycleCommandPayload>(cmdJson);
        Assert.NotNull(cmdRestored);
        Assert.Equal(LifecycleAction.ClockIn, cmdRestored.Action);

        var result = new LifecycleResultPayload(
            true, null, "ok", ONEVO.Agent.Shared.Models.MonitoringState.Active, session);
        var resultJson = JsonSerializer.Serialize(result);
        var resultRestored = JsonSerializer.Deserialize<LifecycleResultPayload>(resultJson);
        Assert.NotNull(resultRestored);
        Assert.True(resultRestored.Success);
        Assert.NotNull(resultRestored.Session);
        Assert.Equal(session.ScheduleDisplay, resultRestored.Session!.ScheduleDisplay);

        var status = new StatusResponsePayload(
            ONEVO.Agent.Shared.Models.MonitoringState.Active,
            DateTimeOffset.UtcNow,
            session);
        var statusJson = JsonSerializer.Serialize(status);
        var statusRestored = JsonSerializer.Deserialize<StatusResponsePayload>(statusJson);
        Assert.NotNull(statusRestored);
        Assert.NotNull(statusRestored.Session);
    }

    [Fact]
    public void DevicePairingStartedPayload_RoundTripsThroughJson()
    {
        var payload = new DevicePairingStartedPayload(
            true, null,
            "https://localhost:4200/device/activate",
            "https://localhost:4200/device/activate?request_id=abc&user_code=XYZ12345",
            600, 5);

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<DevicePairingStartedPayload>(json);

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void DevicePairingResultPayload_RoundTripsThroughJson()
    {
        var payload = new DevicePairingResultPayload
        {
            Success = true,
            ErrorCode = null,
            EmployeeName = "Test Employee",
            EmployeeEmail = "test.employee@test.dev",
            EmployeeNumber = "EMP-TEST-01"
        };

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<DevicePairingResultPayload>(json);

        Assert.Equal(payload, restored);
    }
}
