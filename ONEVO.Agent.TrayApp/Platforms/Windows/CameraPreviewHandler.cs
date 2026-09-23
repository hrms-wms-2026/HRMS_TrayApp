namespace ONEVO.Agent.TrayApp.Platforms.Windows;

using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using ONEVO.Agent.TrayApp.Controls;
using MediaFrameSource = global::Windows.Media.Capture.Frames.MediaFrameSource;
using MediaPlayer = global::Windows.Media.Playback.MediaPlayer;
using MediaPlayerElement = global::Microsoft.UI.Xaml.Controls.MediaPlayerElement;
using MediaSource = global::Windows.Media.Core.MediaSource;
using ElementCompositionPreview = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview;
using ScaleTransform = global::Microsoft.UI.Xaml.Media.ScaleTransform;
using WinGrid = global::Microsoft.UI.Xaml.Controls.Grid;
using WinStretch = global::Microsoft.UI.Xaml.Media.Stretch;

/// <summary>
/// Live camera surface inside the circular face frame.
/// MediaPlayerElement ignores UniformToFill for a frame source and letterboxes the
/// webcam, so the video edge cuts across the mouth. The player is sized to cover the
/// circle (same center crop as the captured still) and the host clips to that circle.
/// </summary>
public sealed class CameraPreviewHandler : ViewHandler<CameraPreview, WinGrid>
{
    private MediaPlayerElement? _playerElement;
    private MediaPlayer? _player;
    private MediaFrameSource? _source;
    private global::Windows.Foundation.TypedEventHandler<MediaFrameSource, object>? _formatChanged;
    private double _aspect = 4d / 3d;

    public static PropertyMapper<CameraPreview, CameraPreviewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(CameraPreview.FrameSource)] = MapFrameSource,
        };

    public CameraPreviewHandler() : base(Mapper) { }

    protected override WinGrid CreatePlatformView()
    {
        _playerElement = new MediaPlayerElement
        {
            Stretch = WinStretch.UniformToFill,
            AreTransportControlsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var host = new WinGrid();
        host.Children.Add(_playerElement);
        host.SizeChanged += (_, _) => FitCover(host);
        return host;
    }

    private static void MapFrameSource(CameraPreviewHandler handler, CameraPreview view)
    {
        handler.ReleasePlayer();

        if (view.FrameSource is not MediaFrameSource source || handler._playerElement is null)
            return;

        handler._source = source;
        handler._aspect = ReadAspect(source);
        handler._formatChanged = (_, _) => handler.OnFormatChanged(source);
        source.FormatChanged += handler._formatChanged;

        var player = new MediaPlayer { RealTimePlayback = true };
        player.Source = MediaSource.CreateFromMediaFrameSource(source);
        handler._player = player;
        handler._playerElement.Stretch = WinStretch.UniformToFill;
        handler._playerElement.SetMediaPlayer(player);
        player.Play();

        if (handler.PlatformView is not null)
            handler.FitCover(handler.PlatformView);
    }

    private void OnFormatChanged(MediaFrameSource source)
    {
        var aspect = ReadAspect(source);
        _aspect = aspect;
        PlatformView?.DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_source, source) || PlatformView is null)
                return;

            FitCover(PlatformView);
        });
    }

    private void FitCover(WinGrid host)
    {
        if (_playerElement is null)
            return;

        if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
            return;

        // The frame-source player letterboxes inside its layout slot and ignores a
        // larger Width/Height, so the 4:3 edge stays across the mouth. Scale the
        // rendered frame from the top until that edge covers the circle.
        var scale = CameraPreviewCover.CoverScale(host.ActualWidth, host.ActualHeight, _aspect);
        _playerElement.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0);
        _playerElement.RenderTransform = new ScaleTransform
        {
            ScaleX = scale,
            ScaleY = scale,
        };
        ClipHostToCircle(host);
    }

    private static void ClipHostToCircle(WinGrid host)
    {
        var width = (float)host.ActualWidth;
        var height = (float)host.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        // WinUI UIElement.Clip only accepts a rectangle. A composition ellipse keeps
        // the oversized cover crop inside the round face frame.
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var ellipse = visual.Compositor.CreateEllipseGeometry();
        ellipse.Center = new System.Numerics.Vector2(width / 2f, height / 2f);
        ellipse.Radius = new System.Numerics.Vector2(width / 2f, height / 2f);
        visual.Clip = visual.Compositor.CreateGeometricClip(ellipse);
    }

    private static double ReadAspect(MediaFrameSource source)
    {
        var video = source.CurrentFormat?.VideoFormat
            ?? source.SupportedFormats
                .Select(format => format.VideoFormat)
                .FirstOrDefault(format => format is { Width: > 0, Height: > 0 });

        if (video is not { Width: > 0, Height: > 0 })
            return 4d / 3d;

        return video.Width / (double)video.Height;
    }

    private void ReleasePlayer()
    {
        if (_source is not null && _formatChanged is not null)
            _source.FormatChanged -= _formatChanged;

        _source = null;
        _formatChanged = null;
        _player?.Pause();
        _player?.Dispose();
        _player = null;
        if (_playerElement is not null)
            _playerElement.SetMediaPlayer(null);
    }

    protected override void DisconnectHandler(WinGrid platformView)
    {
        ReleasePlayer();
        _playerElement = null;
        base.DisconnectHandler(platformView);
    }
}
