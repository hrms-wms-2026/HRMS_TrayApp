using System.Drawing;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using ONEVO.Agent.TrayApp.Capture;
using Color = System.Drawing.Color;

namespace ONEVO.Agent.TrayApp.Tests.Capture;

public sealed class PhotoBrightnessTests
{
    internal static byte[] SolidJpeg(Color color)
    {
        using var bitmap = new Bitmap(64, 48);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    [Fact]
    public void NormalRoom_IsOk()
    {
        Assert.True(PhotoBrightness.IsOk(SolidJpeg(Color.FromArgb(150, 140, 130))));
    }

    [Fact]
    public void DarkRoom_IsNotOk()
    {
        Assert.False(PhotoBrightness.IsOk(SolidJpeg(Color.FromArgb(20, 20, 20))));
    }

    [Fact]
    public void BlownOut_IsNotOk()
    {
        Assert.False(PhotoBrightness.IsOk(SolidJpeg(Color.White)));
    }

    [Fact]
    public void UnreadableBytes_IsUnknown()
    {
        Assert.Null(PhotoBrightness.IsOk([0xFF, 0xD8, 0xFF]));
        Assert.Null(PhotoBrightness.IsOk(null));
    }
}
