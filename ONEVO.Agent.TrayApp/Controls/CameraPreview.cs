namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>Cross-platform placeholder for the live camera preview view.</summary>
public sealed class CameraPreview : View
{
    public static readonly BindableProperty FrameSourceProperty =
        BindableProperty.Create(nameof(FrameSource), typeof(object), typeof(CameraPreview), null);

    /// <summary>
    /// Platform-specific frame source.
    /// Set to a <c>Windows.Media.Capture.Frames.MediaFrameSource</c> to start live preview.
    /// Set to null to stop.
    /// </summary>
    public object? FrameSource
    {
        get => GetValue(FrameSourceProperty);
        set => SetValue(FrameSourceProperty, value);
    }
}
