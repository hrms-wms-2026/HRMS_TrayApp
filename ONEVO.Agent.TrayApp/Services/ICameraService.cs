namespace ONEVO.Agent.TrayApp.Services;

public interface ICameraService
{
    Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default);
}
