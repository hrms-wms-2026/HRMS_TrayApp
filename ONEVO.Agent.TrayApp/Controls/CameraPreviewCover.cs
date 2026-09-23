namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>
/// Sizes a webcam frame so it covers the circular face slot.
/// A square slot with the video's native aspect leaves a hard edge across the mouth.
/// </summary>
public static class CameraPreviewCover
{
    public readonly record struct Size(double Width, double Height);

    public static Size Cover(double slotWidth, double slotHeight, double aspectWidthOverHeight)
    {
        if (slotWidth <= 0 || slotHeight <= 0)
            return new Size(0, 0);

        if (aspectWidthOverHeight <= 0
            || double.IsNaN(aspectWidthOverHeight)
            || double.IsInfinity(aspectWidthOverHeight))
        {
            return new Size(slotWidth, slotHeight);
        }

        var slotAspect = slotWidth / slotHeight;
        if (aspectWidthOverHeight >= slotAspect)
            return new Size(slotHeight * aspectWidthOverHeight, slotHeight);

        return new Size(slotWidth, slotWidth / aspectWidthOverHeight);
    }

    /// <summary>
    /// How far to scale a letterboxed webcam so its short side covers the slot.
    /// A 4:3 frame fitted to a square leaves the mouth on the bottom edge; 4/3 zoom brings it inside.
    /// </summary>
    public static double CoverScale(double slotWidth, double slotHeight, double aspectWidthOverHeight)
    {
        if (slotWidth <= 0 || slotHeight <= 0)
            return 1;

        if (aspectWidthOverHeight <= 0
            || double.IsNaN(aspectWidthOverHeight)
            || double.IsInfinity(aspectWidthOverHeight))
        {
            return 1;
        }

        var slotAspect = slotWidth / slotHeight;
        if (aspectWidthOverHeight >= slotAspect)
            return aspectWidthOverHeight / slotAspect;

        return slotAspect / aspectWidthOverHeight;
    }
}
