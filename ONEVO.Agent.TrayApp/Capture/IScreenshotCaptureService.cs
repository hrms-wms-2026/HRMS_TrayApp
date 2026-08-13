namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing;

/// <summary>
/// Result of a single virtual-desktop screenshot capture attempt (§7.5 — inactivity screenshot).
/// On success, <see cref="JpegBytes"/> holds JPEG-encoded bytes spanning every connected monitor
/// and <see cref="Sha256"/>/<see cref="CapturedAt"/>/<see cref="MonitorCount"/>/
/// <see cref="VirtualBounds"/> describe it; no <see cref="System.Drawing.Bitmap"/> or other GDI+
/// handle ever crosses this boundary. On failure, <see cref="JpegBytes"/> is empty and
/// <see cref="FailureCode"/> carries one of the stable, machine-readable reasons enumerated in
/// <see cref="ScreenshotFailureCodes"/> for callers/telemetry — never a free-form exception
/// message.
/// </summary>
public sealed record ScreenshotCaptureResult(
    bool Success,
    ReadOnlyMemory<byte> JpegBytes,
    DateTimeOffset? CapturedAt,
    int MonitorCount,
    Rectangle VirtualBounds,
    string? Sha256,
    string? FailureCode);

/// <summary>
/// Single source of truth for every <see cref="ScreenshotCaptureResult.FailureCode"/> string a
/// capture attempt can produce. Kept here — beside <see cref="ScreenshotCaptureResult"/>, which
/// owns capture-result semantics — rather than scattered as literals across
/// <see cref="VirtualDesktopScreenshotCaptureService"/>, so producers and any future
/// consumers/telemetry mapping stay DRY.
/// </summary>
public static class ScreenshotFailureCodes
{
    /// <summary><c>Screen.AllScreens</c> returned no monitors.</summary>
    public const string NoDisplays = "no_displays";

    /// <summary>
    /// The virtual desktop bounds collapsed to zero width/height (e.g. a secure desktop or locked
    /// session reporting a degenerate screen).
    /// </summary>
    public const string ZeroBounds = "zero_bounds";

    /// <summary>
    /// The encoded JPEG still exceeded the byte budget after downscaling to the minimum scale
    /// floor.
    /// </summary>
    public const string CaptureTooLarge = "capture_too_large";

    /// <summary>
    /// The WinForms/GDI+ capture or encode call threw (covers secure desktop/locked session cases
    /// where <c>CopyFromScreen</c> itself fails, and any other unexpected capture-API exception).
    /// </summary>
    public const string CaptureApiFailed = "capture_api_failed";
}

/// <summary>
/// Captures a single JPEG image spanning the full virtual desktop (all connected monitors, not
/// just the primary one) for the inactivity-screenshot workflow (§7.5). Implementations own all
/// GDI+/WinForms interop; callers only ever see <see cref="ScreenshotCaptureResult"/>.
/// The actual capture/encode work runs on a background thread (via <c>Task.Run</c> in the real
/// implementation) — callers may invoke this from the UI thread without blocking it.
/// </summary>
public interface IScreenshotCaptureService
{
    Task<ScreenshotCaptureResult> CaptureAsync(CancellationToken ct);
}
