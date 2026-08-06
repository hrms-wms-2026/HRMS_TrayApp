using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class FakeCameraService : ICameraService
{
    public bool ShouldReturnPhoto { get; set; } = true;
    public int CallCount { get; private set; }

    public Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        CallCount++;
        byte[]? result = ShouldReturnPhoto ? [0xFF, 0xD8, 0xFF] : null;
        return Task.FromResult(result);
    }
}
