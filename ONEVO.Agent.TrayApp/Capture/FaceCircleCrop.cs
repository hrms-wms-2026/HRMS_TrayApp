namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing.Drawing2D;
// MAUI's global usings also define Color/Rectangle; this helper works on GDI+ bitmaps.
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using GraphicsUnit = System.Drawing.GraphicsUnit;
using Rectangle = System.Drawing.Rectangle;

/// <summary>
/// Cuts a captured face photo down to what the round preview shows. The preview fills a circle
/// with the centre of the live camera frame (AspectFill), so the still photo is first cut to the
/// preview's aspect ratio (a 4:3 still behind a 16:9 preview), then to the centre square, and
/// everything outside the inscribed circle is painted a flat neutral grey. A person or picture
/// in the background outside the circle then never reaches AWS.
/// </summary>
public static class FaceCircleCrop
{
    /// <summary>Neutral mid-grey: no edges or skin tones for face detection to latch onto.</summary>
    public static readonly Color OutsideFill = Color.FromArgb(128, 128, 128);

    /// <param name="previewAspect">Live preview width / height; null when unknown (treated as the still's own).</param>
    public static Bitmap Apply(Bitmap source, double? previewAspect = null)
    {
        var region = VisibleSquare(source.Width, source.Height, previewAspect);

        var result = new Bitmap(region.Width, region.Height);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(OutsideFill);

        using var circle = new GraphicsPath();
        circle.AddEllipse(0, 0, region.Width, region.Height);
        g.SetClip(circle);
        g.DrawImage(
            source,
            new Rectangle(0, 0, region.Width, region.Height),
            region,
            GraphicsUnit.Pixel);
        return result;
    }

    /// <summary>The square of the still photo that sits behind the round preview.</summary>
    public static Rectangle VisibleSquare(int width, int height, double? previewAspect)
    {
        // Area of the still that the live preview frame covers (centre crop to its aspect).
        double regionWidth = width, regionHeight = height;
        if (previewAspect is > 0)
        {
            var stillAspect = width / (double)height;
            if (stillAspect > previewAspect.Value)
                regionWidth = height * previewAspect.Value;
            else if (stillAspect < previewAspect.Value)
                regionHeight = width / previewAspect.Value;
        }

        // AspectFill into the circle shows that area's centre square.
        var side = (int)Math.Floor(Math.Min(regionWidth, regionHeight));
        return new Rectangle((width - side) / 2, (height - side) / 2, side, side);
    }
}
