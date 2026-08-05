namespace ONEVO.Agent.Shared.Models;

using System.Text.Json;

public sealed record CollectionRecord
{
    public required string EventId { get; init; }
    public required string RecordType { get; init; }
    public required string SchemaVersion { get; init; }
    public DateTimeOffset CaptureTimestamp { get; init; }
    public required string DeviceId { get; init; }
    public required JsonElement Payload { get; init; }
}
