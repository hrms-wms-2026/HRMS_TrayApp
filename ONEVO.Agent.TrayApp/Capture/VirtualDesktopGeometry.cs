namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing;

/// <summary>
/// Pure geometry math for combining per-monitor bounds into a single virtual-desktop rectangle.
/// Deliberately takes plain <see cref="Rectangle"/> values rather than WinForms <c>Screen</c>
/// instances so it can be unit-tested with synthetic monitor layouts — including monitors left
/// of/above the primary, which have negative X/Y origins — without any real display attached.
/// </summary>
public static class VirtualDesktopGeometry
{
    /// <summary>
    /// Returns the smallest rectangle that contains every monitor bounds in
    /// <paramref name="screens"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="screens"/> is null or empty. This is plain geometry-math failure
    /// vocabulary — it deliberately knows nothing about
    /// <see cref="ScreenshotCaptureResult.FailureCode"/> semantics; callers that need a stable
    /// failure code map this condition themselves (see <see cref="ScreenshotFailureCodes.NoDisplays"/>).
    /// </exception>
    public static Rectangle Union(IReadOnlyList<Rectangle> screens)
    {
        if (screens is null || screens.Count == 0)
            throw new InvalidOperationException("Cannot compute virtual desktop bounds: no monitors were supplied.");

        var bounds = screens[0];
        for (var i = 1; i < screens.Count; i++)
            bounds = Rectangle.Union(bounds, screens[i]);

        return bounds;
    }
}
