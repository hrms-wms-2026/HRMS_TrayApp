namespace ONEVO.Agent.TrayApp.Services;

public interface ICameraService
{
    /// <summary>JPEG frames for the circular live preview. Raised on the UI thread.</summary>
    event EventHandler<byte[]>? PreviewFrame;

    Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default);

    /// <summary>
    /// Initializes the camera and returns the native preview frame source.
    /// On Windows returns <c>Windows.Media.Capture.Frames.MediaFrameSource</c>; null on failure.
    /// </summary>
    Task<object?> StartPreviewAsync(CancellationToken ct = default);

    Task StopPreviewAsync();
}
