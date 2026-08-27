namespace ONEVO.Agent.Shared;

public static class Constants
{
    public const string PipeName = "ONEVO.Agent.Pipe.v1";
    public const int MaxMessageLengthBytes = 65_536;
    public const int IpcConnectionTimeoutMs = 5_000;
    public const int NonceLengthBytes = 32;
    public const int ReconnectMaxAttempts = 5;
    public const int ReconnectBaseDelayMs = 1_000;

    /// <summary>
    /// Once the initial exponential-backoff burst (<see cref="ReconnectMaxAttempts"/> tries) is
    /// exhausted, the client keeps retrying at this fixed cadence indefinitely rather than giving
    /// up permanently — the Service can come back (crash recovery, restart, update) well after
    /// the burst window, and the Tray App must not require a manual relaunch to notice.
    /// </summary>
    public const int ReconnectSteadyStateDelayMs = 15_000;

    /// <summary>Default activity capture interval (seconds). Max backend interval is 300.</summary>
    public const int DefaultActivitySnapshotIntervalSeconds = 60;

    /// <summary>Max keyboard/mouse events counted per interval before soft-cap (privacy + overflow).</summary>
    public const int MaxEventsPerInterval = 100_000;

    /// <summary>Size (bytes) of each evidence-transfer chunk sent over IPC.</summary>
    public const int EvidenceChunkSizeBytes = 32_768;

    /// <summary>Max allowed size (bytes) of a single inactivity screenshot.</summary>
    public const int MaxScreenshotBytes = 10_485_760;

    /// <summary>
    /// Max raw JPEG size (bytes) for a clock-in face photo. Unlike inactivity screenshots, this
    /// image travels as one base64 field inside a single <see cref="MaxMessageLengthBytes"/> IPC
    /// line rather than through the chunked evidence-transfer protocol, so it must stay small
    /// enough that base64 inflation (~4/3) plus JSON envelope overhead still fits — 40 KB raw
    /// encodes to ~53 KB, leaving well over 10 KB of headroom under the 64 KB line cap.
    /// </summary>
    public const int MaxFacePhotoJpegBytes = 40_000;
}
