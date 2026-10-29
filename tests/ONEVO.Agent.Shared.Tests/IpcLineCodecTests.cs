namespace ONEVO.Agent.Shared.Tests;

using System.Text;
using ONEVO.Agent.Shared.IPC;
using Xunit;

public class IpcLineCodecTests
{
    [Fact]
    public void TryParse_RoundTripsSerializedEnvelope()
    {
        var line = IpcLineCodec.Serialize(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest, CorrelationId = "c1" });

        Assert.True(IpcLineCodec.TryParse(line, out var envelope));
        Assert.Equal(IpcMessageTypes.StatusRequest, envelope!.Type);
        Assert.Equal("c1", envelope.CorrelationId);
    }

    [Fact]
    public void TryParse_AcceptsLeadingBom_FromOlderPeers()
    {
        // ISS-006: older peers created a StreamWriter(Encoding.UTF8) per phase; each one
        // writes a BOM on a non-seekable pipe, so a mid-stream line starts with U+FEFF.
        var line = "﻿" + IpcLineCodec.Serialize(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest });

        Assert.True(IpcLineCodec.TryParse(line, out var envelope));
        Assert.Equal(IpcMessageTypes.StatusRequest, envelope!.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("﻿")]
    [InlineData("not-json")]
    [InlineData("{\"v\":\"1.0\"}")] // missing required "type"
    [InlineData("null")]
    public void TryParse_RejectsInvalidInput_WithoutThrowing(string? line)
    {
        Assert.False(IpcLineCodec.TryParse(line, out var envelope));
        Assert.Null(envelope);
    }

    [Fact]
    public void TryParse_RejectsOversizedLine()
    {
        var padding = new string('x', Constants.MaxMessageLengthBytes);
        var line = $"{{\"type\":\"StatusRequest\",\"pad\":\"{padding}\"}}";

        Assert.False(IpcLineCodec.TryParse(line, out _));
    }

    [Fact]
    public async Task Writer_DoesNotEmitBom()
    {
        using var stream = new MemoryStream();
        await using (var writer = IpcLineCodec.CreateWriter(stream))
            await writer.WriteLineAsync("{}");

        var bytes = stream.ToArray();
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal("{}" + Environment.NewLine, Encoding.UTF8.GetString(bytes));
    }
}
