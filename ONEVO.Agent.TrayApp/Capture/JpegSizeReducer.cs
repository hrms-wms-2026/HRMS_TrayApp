namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing;
using System.Drawing.Imaging;
using Encoder = System.Drawing.Imaging.Encoder;

/// <summary>
/// Result of one <see cref="JpegSizeReducer.Encode"/> call.
/// </summary>
public readonly record struct JpegSizeReductionResult(
    bool Success,
    ReadOnlyMemory<byte> JpegBytes,
    double AppliedScale);

/// <summary>
/// Encodes an in-memory <see cref="Bitmap"/> as JPEG at a fixed, explicitly controlled quality
/// (see <see cref="JpegQuality"/> — <c>Bitmap.Save(stream, ImageFormat.Jpeg)</c> alone uses an
/// undocumented GDI+ default, not a real quality=75), proportionally downscaling and re-encoding
/// until the result fits <c>maxBytes</c> or the minimum scale floor (<see cref="MinScale"/>) is
/// reached. Takes a plain <see cref="Bitmap"/> rather than performing its own screen capture so
/// it can be exercised in tests against small synthetic bitmaps, with no real display required.
/// </summary>
public static class JpegSizeReducer
{
    /// <summary>Fixed JPEG encode quality (0-100) used for every capture.</summary>
    public const long JpegQuality = 75L;

    /// <summary>
    /// Smallest proportional scale attempted before giving up. Once a re-encode at this scale is
    /// still over budget, <see cref="Encode"/> returns a failed result — this is what bounds the
    /// downscaling loop and guarantees it terminates.
    /// </summary>
    public const double MinScale = 0.35;

    private const double ScaleStep = 0.85;

    /// <summary>
    /// Encodes <paramref name="source"/> as JPEG, downscaling proportionally until the encoded
    /// byte count is at most <paramref name="maxBytes"/> or <see cref="MinScale"/> is reached.
    /// </summary>
    public static JpegSizeReductionResult Encode(Bitmap source, int maxBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var scale = 1.0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Bitmap? scaled = scale < 1.0 ? Resize(source, scale) : null;
            try
            {
                var bytes = EncodeJpeg(scaled ?? source);
                if (bytes.Length <= maxBytes)
                    return new JpegSizeReductionResult(true, bytes, scale);
            }
            finally
            {
                scaled?.Dispose();
            }

            // Still oversized at the floor scale — this can't be satisfied by downscaling
            // further, so stop rather than loop forever.
            if (scale <= MinScale)
                return new JpegSizeReductionResult(false, ReadOnlyMemory<byte>.Empty, scale);

            scale = Math.Max(MinScale, scale * ScaleStep);
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);

        using var ms = new MemoryStream();
        bitmap.Save(ms, codec, parameters);
        return ms.ToArray();
    }

    private static Bitmap Resize(Bitmap source, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return resized;
    }
}
