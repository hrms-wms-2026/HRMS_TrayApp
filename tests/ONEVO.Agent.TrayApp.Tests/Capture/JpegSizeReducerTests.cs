namespace ONEVO.Agent.TrayApp.Tests.Capture;

using System.Drawing;
using System.Drawing.Imaging;
using ONEVO.Agent.TrayApp.Capture;

public sealed class JpegSizeReducerTests
{
    [Fact]
    public void Encode_ProducesJpegOutput()
    {
        using var bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(Color.CornflowerBlue);

        var result = JpegSizeReducer.Encode(bitmap, maxBytes: int.MaxValue);

        Assert.True(result.Success);
        var bytes = result.JpegBytes.Span;
        Assert.True(bytes.Length > 2);
        // JPEG (JFIF) files start with the SOI marker 0xFFD8.
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    [Fact]
    public void Encode_OversizeOutput_IsProportionallyReduced()
    {
        using var bitmap = CreateNoiseBitmap(150, 150);

        var full = JpegSizeReducer.Encode(bitmap, maxBytes: int.MaxValue);
        var maxBytes = full.JpegBytes.Length / 2;

        var reduced = JpegSizeReducer.Encode(bitmap, maxBytes);

        Assert.True(reduced.Success);
        Assert.True(reduced.AppliedScale < 1.0);
        Assert.True(reduced.AppliedScale >= JpegSizeReducer.MinScale);
        Assert.True(reduced.JpegBytes.Length <= maxBytes);
    }

    [Fact]
    public void Encode_StillOversizeAtMinScale_FailsAsTooLarge()
    {
        using var bitmap = CreateNoiseBitmap(150, 150);

        // A near-zero byte budget can never be satisfied even at the minimum scale floor, so
        // this must terminate as a failure rather than loop forever.
        var result = JpegSizeReducer.Encode(bitmap, maxBytes: 10);

        Assert.False(result.Success);
        Assert.Equal(0, result.JpegBytes.Length);
        Assert.Equal(JpegSizeReducer.MinScale, result.AppliedScale);
    }

    [Fact]
    public void Encode_Cancelled_ProducesNoBytes()
    {
        using var bitmap = new Bitmap(10, 10, PixelFormat.Format32bppArgb);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => JpegSizeReducer.Encode(bitmap, maxBytes: int.MaxValue, cts.Token));
    }

    private static Bitmap CreateNoiseBitmap(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rng = new Random(12345);
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            bmp.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
        return bmp;
    }
}
