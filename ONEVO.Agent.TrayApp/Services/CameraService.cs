namespace ONEVO.Agent.TrayApp.Services;

using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

public sealed class CameraService : ICameraService
{
    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            using var stream = new InMemoryRandomAccessStream();
            var props = ImageEncodingProperties.CreateJpeg();
            await capture.CapturePhotoToStreamAsync(props, stream);
            capture.Dispose();

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
