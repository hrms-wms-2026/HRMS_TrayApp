namespace ONEVO.Agent.Shared.Models;

public sealed record FacePhotoPayload
{
    public required string Format { get; init; }
    public required string Data { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? LocationAccuracy { get; init; }
    public string? LocationAddress { get; init; }
}
