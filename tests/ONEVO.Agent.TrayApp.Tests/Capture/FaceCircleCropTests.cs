using System.Drawing;
using ONEVO.Agent.TrayApp.Capture;
using Color = System.Drawing.Color;

namespace ONEVO.Agent.TrayApp.Tests.Capture;

public sealed class FaceCircleCropTests
{
    [Fact]
    public void SameAspectAsPreview_IsCentreSquareOfFullHeight()
    {
        var square = FaceCircleCrop.VisibleSquare(1280, 720, 1280 / 720d);

        Assert.Equal(new System.Drawing.Rectangle(280, 0, 720, 720), square);
    }

    [Fact]
    public void FourByThreeStill_Behind16By9Preview_UsesPreviewsVisibleHeight()
    {
        // 1440x1080 still; the 16:9 preview only covers its centre 1440x810 band,
        // so the round preview shows an 810px square, not the full 1080.
        var square = FaceCircleCrop.VisibleSquare(1440, 1080, 16 / 9d);

        Assert.Equal(new System.Drawing.Rectangle(315, 135, 810, 810), square);
    }

    [Fact]
    public void UnknownPreviewAspect_FallsBackToStillsCentreSquare()
    {
        var square = FaceCircleCrop.VisibleSquare(1440, 1080, null);

        Assert.Equal(new System.Drawing.Rectangle(180, 0, 1080, 1080), square);
    }

    [Fact]
    public void WideFrame_BecomesCentreSquare_WithOutsideOfCircleGreyedOut()
    {
        // 160x120 frame: a red strip (a "person") in the 20px the centre-square crop drops, rest blue.
        using var source = new Bitmap(160, 120);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Blue);
            g.FillRectangle(Brushes.Red, 0, 0, 20, 120);
        }

        using var result = FaceCircleCrop.Apply(source);

        Assert.Equal(120, result.Width);
        Assert.Equal(120, result.Height);
        // Centre keeps the camera image.
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(60, 60).ToArgb());
        // Corners (outside the round preview) are neutral grey, not camera pixels.
        Assert.Equal(FaceCircleCrop.OutsideFill.ToArgb(), result.GetPixel(1, 1).ToArgb());
        Assert.Equal(FaceCircleCrop.OutsideFill.ToArgb(), result.GetPixel(118, 118).ToArgb());
        // Nothing of the red strip outside the centre square survives.
        for (var x = 0; x < result.Width; x += 4)
            for (var y = 0; y < result.Height; y += 4)
                Assert.NotEqual(Color.Red.ToArgb(), result.GetPixel(x, y).ToArgb());
    }
}
