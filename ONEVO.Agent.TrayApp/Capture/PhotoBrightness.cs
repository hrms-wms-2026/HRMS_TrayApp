namespace ONEVO.Agent.TrayApp.Capture;

using System.Drawing;

/// <summary>
/// Whole-photo brightness, used for the "Good lighting" check when AWS could not judge it:
/// with no usable face, Rekognition has no face brightness to report, and a half-visible face
/// (hair, forehead) gives a misleading one. This is only a UI hint — the clock-in gate is
/// still the backend's AWS result.
/// </summary>
public static class PhotoBrightness
{
    /// <summary>Mean luminance below this (0-100) reads as a dark room.</summary>
    public const double MinOkPercent = 25;

    /// <summary>Mean luminance above this (0-100) reads as a blown-out frame.</summary>
    public const double MaxOkPercent = 92;

    private const int SampleGrid = 48;

    /// <summary>Mean luminance 0-100, or null when the bytes are not a readable image.</summary>
    public static double? MeanPercent(byte[]? image)
    {
        if (image is not { Length: > 0 })
            return null;

        try
        {
            using var stream = new MemoryStream(image);
            using var bitmap = new Bitmap(stream);
            if (bitmap.Width == 0 || bitmap.Height == 0)
                return null;

            // A fixed grid of samples is plenty for an average and keeps GetPixel cheap.
            double total = 0;
            var count = 0;
            for (var gy = 0; gy < SampleGrid; gy++)
            {
                var y = (int)((gy + 0.5) * bitmap.Height / SampleGrid);
                for (var gx = 0; gx < SampleGrid; gx++)
                {
                    var x = (int)((gx + 0.5) * bitmap.Width / SampleGrid);
                    var c = bitmap.GetPixel(x, y);
                    total += (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
                    count++;
                }
            }

            return count == 0 ? null : total / count / 255d * 100d;
        }
        catch
        {
            return null;
        }
    }

    public static bool? IsOk(byte[]? image) =>
        MeanPercent(image) is { } percent
            ? percent is >= MinOkPercent and <= MaxOkPercent
            : null;
}
