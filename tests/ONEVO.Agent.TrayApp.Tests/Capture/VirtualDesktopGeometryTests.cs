namespace ONEVO.Agent.TrayApp.Tests.Capture;

using System.Drawing;
using ONEVO.Agent.TrayApp.Capture;

public sealed class VirtualDesktopGeometryTests
{
    [Fact]
    public void Union_supports_monitors_left_of_primary()
    {
        var bounds = VirtualDesktopGeometry.Union([
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(0, 0, 2560, 1440)]);

        Assert.Equal(new Rectangle(-1920, 0, 4480, 1440), bounds);
    }

    [Fact]
    public void Union_SingleMonitor_ReturnsItsOwnBounds()
    {
        var rect = new Rectangle(0, 0, 1920, 1080);

        var bounds = VirtualDesktopGeometry.Union([rect]);

        Assert.Equal(rect, bounds);
    }

    [Fact]
    public void Union_EmptyList_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => VirtualDesktopGeometry.Union(Array.Empty<Rectangle>()));

        // Plain geometry-math failure vocabulary — deliberately not a shared FailureCode string
        // (see ScreenshotFailureCodes for the capture-result vocabulary callers map onto).
        Assert.Contains("no monitors", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
