namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// In-memory hand-off of the just-captured selfie between PhotoCaptureWindow and
/// IdentityVerificationPage during the clock-in flow. Not persisted, not the photo
/// that gets uploaded — that submit still happens from PhotoCaptureWindowViewModel.
/// </summary>
public sealed class CapturedPhotoBuffer
{
    public byte[]? Bytes { get; set; }
}
