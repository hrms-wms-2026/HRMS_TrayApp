using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class FakeCameraService : ICameraService
{
    public event EventHandler<byte[]>? PreviewFrame;

    public void PublishPreview(byte[] jpeg) => PreviewFrame?.Invoke(this, jpeg);

    public bool ShouldReturnPhoto { get; set; } = true;

    /// <summary>Bytes returned by a successful capture; defaults to a 3-byte JPEG stub.</summary>
    public byte[] PhotoBytes { get; set; } = [0xFF, 0xD8, 0xFF];
    public int CallCount { get; private set; }
    public int PreviewStartCount { get; private set; }
    public int PreviewStopCount { get; private set; }

    public Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        CallCount++;
        byte[]? result = ShouldReturnPhoto ? PhotoBytes : null;
        return Task.FromResult(result);
    }

    public Task<object?> StartPreviewAsync(CancellationToken ct = default)
    {
        PreviewStartCount++;
        return Task.FromResult<object?>(null);
    }

    public Task StopPreviewAsync()
    {
        PreviewStopCount++;
        return Task.CompletedTask;
    }
}
