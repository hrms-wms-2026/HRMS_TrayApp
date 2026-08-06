namespace ONEVO.Agent.TrayApp.Services;

public sealed class CameraService : ICameraService
{
    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Take Profile Photo"
            });
            if (result is null) return null;
            await using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
