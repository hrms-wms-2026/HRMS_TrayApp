namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Windows.Forms;

/// <summary>
/// Real <see cref="IScreenshotCaptureService"/> backed by the WinForms/GDI+ APIs already used by
/// <see cref="ONEVO.Agent.TrayApp.Collectors.ScreenshotCollector"/> for single-monitor capture,
/// generalized here to the full virtual desktop. Depends on the static <c>Screen.AllScreens</c>
/// and <c>SystemInformation.VirtualScreen</c> APIs, which cannot be mocked — like
/// <c>WindowsInactivityPromptService</c>, this class is a thin orchestrator over the
/// independently unit-tested <see cref="VirtualDesktopGeometry"/> and <see cref="JpegSizeReducer"/>
/// and is only exercised manually/via smoke test, not by automated unit tests.
/// <see cref="CaptureAsync"/> offloads the bitmap allocation, <c>CopyFromScreen</c>, and JPEG
/// encode/downscale loop onto a background thread via <see cref="Task.Run(Action, CancellationToken)"/>
/// so callers (e.g. the tray app's UI thread) never block for the capture+encode duration.
/// </summary>
public sealed class VirtualDesktopScreenshotCaptureService : IScreenshotCaptureService
{
    private readonly ILogger<VirtualDesktopScreenshotCaptureService> _logger;

    public VirtualDesktopScreenshotCaptureService(ILogger<VirtualDesktopScreenshotCaptureService> logger)
    {
        _logger = logger;
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(CancellationToken ct) =>
        Task.Run(() => CaptureCore(ct), ct);

    private ScreenshotCaptureResult CaptureCore(CancellationToken ct)
    {
        var screens = Array.Empty<Screen>();
        var virtualBounds = Rectangle.Empty;
        try
        {
            ct.ThrowIfCancellationRequested();

            screens = Screen.AllScreens;
            if (screens.Length == 0)
                return Failure(ScreenshotFailureCodes.NoDisplays);

            // SystemInformation.VirtualScreen is the OS-authoritative combined bounds (correct
            // origin/size across negative-X/Y monitor layouts) and is what CopyFromScreen must be
            // driven from — per-monitor Union is only a defensive fallback for the rare case
            // VirtualScreen comes back degenerate (e.g. some remote-desktop sessions).
            virtualBounds = SystemInformation.VirtualScreen;
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                virtualBounds = VirtualDesktopGeometry.Union(screens.Select(s => s.Bounds).ToArray());

            // Still degenerate after the Union fallback (e.g. secure desktop / locked session
            // reporting a collapsed virtual screen) — fail explicitly with metadata rather than
            // letting the Bitmap constructor throw an opaque ArgumentException.
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                return Failure(ScreenshotFailureCodes.ZeroBounds, screens.Length, virtualBounds);

            using var bmp = new Bitmap(virtualBounds.Width, virtualBounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                // Pass the virtual origin explicitly — it is frequently negative (monitors left
                // of/above the primary), never assume (0,0).
                g.CopyFromScreen(virtualBounds.Location, Point.Empty, virtualBounds.Size);
            }

            ct.ThrowIfCancellationRequested();

            var encoded = JpegSizeReducer.Encode(bmp, Constants.MaxScreenshotBytes, ct);
            if (!encoded.Success)
            {
                _logger.LogWarning(
                    "Virtual desktop screenshot still exceeds {MaxBytes} bytes at minimum scale {MinScale}",
                    Constants.MaxScreenshotBytes, JpegSizeReducer.MinScale);
                return Failure(ScreenshotFailureCodes.CaptureTooLarge, screens.Length, virtualBounds);
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(encoded.JpegBytes.Span)).ToLowerInvariant();

            return new ScreenshotCaptureResult(
                Success: true,
                JpegBytes: encoded.JpegBytes,
                CapturedAt: DateTimeOffset.UtcNow,
                MonitorCount: screens.Length,
                VirtualBounds: virtualBounds,
                Sha256: sha256,
                FailureCode: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Virtual desktop screenshot capture failed");
            // screens/virtualBounds reflect whatever was already resolved before the failure
            // (most commonly CopyFromScreen throwing on a locked/secure desktop after monitor
            // enumeration succeeded) — pass them through instead of discarding known metadata.
            return Failure(ScreenshotFailureCodes.CaptureApiFailed, screens.Length, virtualBounds);
        }
    }

    private static ScreenshotCaptureResult Failure(string code, int monitorCount = 0, Rectangle virtualBounds = default) =>
        new(
            Success: false,
            JpegBytes: ReadOnlyMemory<byte>.Empty,
            CapturedAt: null,
            MonitorCount: monitorCount,
            VirtualBounds: virtualBounds,
            Sha256: null,
            FailureCode: code);
}
