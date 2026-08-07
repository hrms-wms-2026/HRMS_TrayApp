namespace ONEVO.Agent.TrayApp.Services;

using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

public sealed class CameraService : ICameraService
{
    private MediaCapture? _sharedCapture;

    public async Task<object?> StartPreviewAsync(CancellationToken ct = default)
    {
        if (_sharedCapture is not null) return GetColorSource();
        try
        {
            _sharedCapture = new MediaCapture();
            await _sharedCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video
            });
            return GetColorSource();
        }
        catch
        {
            _sharedCapture?.Dispose();
            _sharedCapture = null;
            return null;
        }
    }

    private object? GetColorSource() => _sharedCapture?.FrameSources.Values
        .FirstOrDefault(s => s.Info.MediaStreamType == MediaStreamType.VideoPreview
                          || s.Info.MediaStreamType == MediaStreamType.VideoRecord);

    public Task StopPreviewAsync()
    {
        if (_sharedCapture is null) return Task.CompletedTask;
        var c = _sharedCapture;
        _sharedCapture = null;
        c.Dispose();
        return Task.CompletedTask;
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

            using var stream = new InMemoryRandomAccessStream();
            await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);

            if (ownCapture) capture.Dispose();

            stream.Seek(0);
            var bytes = new byte[stream.Size];
            await stream.AsStreamForRead().ReadExactlyAsync(bytes, 0, bytes.Length, ct);
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
