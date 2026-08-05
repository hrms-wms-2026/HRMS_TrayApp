namespace ONEVO.Agent.Shared.IPC;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record IpcEnvelope
{
    [JsonPropertyName("v")]
    public string Version { get; init; } = IpcProtocolVersion.Current;

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("cid")]
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}
