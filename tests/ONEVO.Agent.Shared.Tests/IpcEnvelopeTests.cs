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
}
