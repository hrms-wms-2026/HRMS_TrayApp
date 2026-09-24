namespace ONEVO.Agent.Shared.IPC;

/// <summary>Tray → Service: begin a working-hours screenshot upload.</summary>
public sealed record PeriodicScreenshotStartPayload(
    Guid CaptureId,
    DateTimeOffset CapturedAt,
    int TotalBytes,
    int ChunkCount);

/// <summary>Tray → Service: one base64-encoded chunk of a working-hours screenshot.</summary>
public sealed record PeriodicScreenshotChunkPayload(Guid CaptureId, int Index, string DataBase64);

/// <summary>Tray → Service: all chunks for the screenshot have been sent.</summary>
public sealed record PeriodicScreenshotCompletePayload(Guid CaptureId);
