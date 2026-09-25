namespace ONEVO.Agent.TrayApp.Services;

using System.Drawing;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using ONEVO.Agent.Shared;
using ONEVO.Agent.TrayApp.Capture;

public sealed class CameraService : ICameraService
{
    private const int PreviewMaxEdge = 480;
    private const long PreviewFrameIntervalMs = 120;

    private MediaCapture? _sharedCapture;
    private MediaFrameReader? _reader;

    /// <summary>Width / height of the live preview frames — what the round preview is cut from.</summary>
    private double? _previewAspect;
    private int _frameBusy;
    private int _loggedFrame;
    private long _lastFrameTick;

    public event EventHandler<byte[]>? PreviewFrame;

    public async Task<object?> StartPreviewAsync(CancellationToken ct = default)
    {
        if (_sharedCapture is not null)
            return await StartReaderAsync(ct) ? null : GetColorSource();

        try
        {
            ct.ThrowIfCancellationRequested();
            _sharedCapture = new MediaCapture();
            await _sharedCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            // A running reader fills the circle with AspectFill frames. If the camera
            // cannot deliver CPU frames, fall back to the native preview surface.
            if (await StartReaderAsync(ct))
                return null;

            return GetColorSource();
        }
        catch
        {
            await StopPreviewAsync();
            return null;
        }
    }

    private MediaFrameSource? GetColorSource()
    {
        var sources = _sharedCapture?.FrameSources.Values;
        if (sources is null)
            return null;

        // Infrared or a record pin can be enumerated first and crops the mouth.
        return sources.FirstOrDefault(s =>
                s.Info.SourceKind == MediaFrameSourceKind.Color
                && s.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? sources.FirstOrDefault(s =>
                s.Info.SourceKind == MediaFrameSourceKind.Color
                && s.Info.MediaStreamType == MediaStreamType.VideoRecord)
            ?? sources.FirstOrDefault(s =>
                s.Info.MediaStreamType == MediaStreamType.VideoPreview
                || s.Info.MediaStreamType == MediaStreamType.VideoRecord);
    }

    private async Task<bool> StartReaderAsync(CancellationToken ct)
    {
        if (_reader is not null)
            return true;

        var source = GetColorSource();
        if (source is null || _sharedCapture is null)
            return false;

        try
        {
            var format = ChoosePreviewFormat(source);
            if (format is not null)
                await source.SetFormatAsync(format).AsTask(ct);
        }
        catch (Exception ex)
        {
            BootLog($"preview format skipped: {ex.Message}");
        }

        if (await TryStartReaderAsync(source, MediaEncodingSubtypes.Bgra8, ct))
            return true;

        return await TryStartReaderAsync(source, subtype: null, ct);
    }

    private async Task<bool> TryStartReaderAsync(MediaFrameSource source, string? subtype, CancellationToken ct)
    {
        try
        {
            _reader = subtype is null
                ? await _sharedCapture!.CreateFrameReaderAsync(source).AsTask(ct)
                : await _sharedCapture!.CreateFrameReaderAsync(source, subtype).AsTask(ct);
            _reader.FrameArrived += OnFrameArrived;
            var status = await _reader.StartAsync().AsTask(ct);
            if (status == MediaFrameReaderStartStatus.Success)
            {
                var video = source.CurrentFormat?.VideoFormat;
                if (video is { Width: > 0, Height: > 0 })
                    _previewAspect = video.Width / (double)video.Height;
                BootLog($"preview reader started {video?.Width}x{video?.Height} subtype={subtype ?? "native"}");
                return true;
            }

            BootLog($"preview reader {status} subtype={subtype ?? "native"}");
        }
        catch (Exception ex)
        {
            BootLog($"preview reader failed subtype={subtype ?? "native"}: {ex.Message}");
        }

        await StopReaderAsync();
        return false;
    }

    private static MediaFrameFormat? ChoosePreviewFormat(MediaFrameSource source)
    {
        var formats = source.SupportedFormats
            .Where(f => f.VideoFormat is { Width: >= 640, Height: >= 480 })
            .ToList();
        if (formats.Count == 0)
            return null;

        // Keep the still-photo field of view. A tiny preview mode is a tighter crop and cuts the chin.
        var largest = formats
            .OrderByDescending(f => (long)f.VideoFormat.Width * f.VideoFormat.Height)
            .First();
        var aspect = largest.VideoFormat.Width / (double)largest.VideoFormat.Height;
        return formats
            .OrderBy(f => Math.Abs((f.VideoFormat.Width / (double)f.VideoFormat.Height) - aspect))
            .ThenBy(f => Math.Abs(f.VideoFormat.Width - 960))
            .First();
    }

    public async Task StopPreviewAsync()
    {
        await StopReaderAsync();
        if (_sharedCapture is null)
            return;

        var capture = _sharedCapture;
        _sharedCapture = null;
        capture.Dispose();
    }

    private async Task StopReaderAsync()
    {
        var reader = _reader;
        _reader = null;
        if (reader is null)
            return;

        reader.FrameArrived -= OnFrameArrived;
        try { await reader.StopAsync(); }
        catch { /* already stopped */ }
        reader.Dispose();
    }

    private async void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _frameBusy, 1, 0) != 0)
        {
            using var dropped = sender.TryAcquireLatestFrame();
            return;
        }

        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var now = Environment.TickCount64;
            if (now - _lastFrameTick < PreviewFrameIntervalMs)
                return;

            var video = frame?.VideoMediaFrame;
            using var copied = video?.SoftwareBitmap is null && video?.Direct3DSurface is not null
                ? await SoftwareBitmap.CreateCopyFromSurfaceAsync(video.Direct3DSurface)
                : null;
            var bitmap = video?.SoftwareBitmap ?? copied;
            if (bitmap is null)
                return;

            using var bgra = SoftwareBitmap.Convert(
                bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var jpeg = await EncodePreviewJpegAsync(bgra);
            if (jpeg is not { Length: > 0 })
                return;

            _lastFrameTick = now;
            if (Interlocked.Exchange(ref _loggedFrame, 1) == 0)
                BootLog($"preview frame {bitmap.PixelWidth}x{bitmap.PixelHeight}");
            var handler = PreviewFrame;
            if (handler is null)
                return;

            MainThread.BeginInvokeOnMainThread(() => handler(this, jpeg));
        }
        catch
        {
            // Drop a bad frame. The next arrival retries.
        }
        finally
        {
            Interlocked.Exchange(ref _frameBusy, 0);
        }
    }

    private static async Task<byte[]?> EncodePreviewJpegAsync(SoftwareBitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width > PreviewMaxEdge || height > PreviewMaxEdge)
        {
            var scale = PreviewMaxEdge / (double)Math.Max(width, height);
            encoder.BitmapTransform.ScaledWidth = Even(width * scale);
            encoder.BitmapTransform.ScaledHeight = Even(height * scale);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
        }

        await encoder.FlushAsync();
        var length = (int)stream.Size;
        if (length <= 0)
            return null;

        stream.Seek(0);
        var bytes = new byte[length];
        using var net = stream.AsStreamForRead();
        await net.ReadExactlyAsync(bytes);
        return bytes;
    }

    private static uint Even(double value)
    {
        var rounded = Math.Max(2, (int)value);
        return (uint)(rounded / 2 * 2);
    }

    private static void BootLog(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ONEVO", "Agent", "tray-boot.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [Camera] {message}{Environment.NewLine}");
        }
        catch
        {
            // Preview must not fail because the log file is locked.
        }
    }

    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var ownCapture = _sharedCapture is null;
            var capture    = _sharedCapture ?? new MediaCapture();

            if (ownCapture)
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video
                });
            }

            await StopReaderAsync();
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
                stream.Seek(0);

                if (ownCapture)
                    capture.Dispose();

                // The raw capture (full webcam resolution) routinely exceeds the single-line IPC
                // message cap once base64-encoded. Downscale/re-encode to a bounded size the same
                // way screenshots do (see JpegSizeReducer) rather than shipping it as-is.
                using var bitmap = new Bitmap(stream.AsStreamForRead());
                // Send only what the employee saw inside the circle: people or photos in the
                // background outside it must not fail the check as "another person".
                using var circle = FaceCircleCrop.Apply(bitmap, _previewAspect);
                BootLog($"photo {bitmap.Width}x{bitmap.Height} preview aspect {_previewAspect:0.###} -> sent {circle.Width}x{circle.Height}");
                var encoded = JpegSizeReducer.Encode(circle, Constants.MaxFacePhotoJpegBytes, ct);
                return encoded.Success ? encoded.JpegBytes.ToArray() : null;
            }
            finally
            {
                if (!ownCapture && _sharedCapture is not null)
                    await StartReaderAsync(CancellationToken.None);
            }
        }
        catch
        {
            return null;
        }
    }
}
