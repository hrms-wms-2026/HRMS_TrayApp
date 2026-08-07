namespace ONEVO.Agent.TrayApp.Platforms.Windows;

using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using ONEVO.Agent.TrayApp.Controls;
using MediaFrameSource  = global::Windows.Media.Capture.Frames.MediaFrameSource;
using MediaSource       = global::Windows.Media.Core.MediaSource;
using MediaPlayer       = global::Windows.Media.Playback.MediaPlayer;

public sealed class CameraPreviewHandler
    : ViewHandler<CameraPreview, MediaPlayerElement>
{
    private MediaPlayer? _player;

    public static PropertyMapper<CameraPreview, CameraPreviewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(CameraPreview.FrameSource)] = MapFrameSource,
        };

    public CameraPreviewHandler() : base(Mapper) { }

    protected override MediaPlayerElement CreatePlatformView() =>
        new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };

    private static void MapFrameSource(CameraPreviewHandler h, CameraPreview view)
    {
        if (view.FrameSource is MediaFrameSource source)
        {
            var player = new MediaPlayer();
            player.Source = MediaSource.CreateFromMediaFrameSource(source);
            h._player = player;
            h.PlatformView.SetMediaPlayer(player);
            player.Play();
        }
        else
        {
            h._player?.Pause();
            h._player?.Dispose();
            h._player = null;
            h.PlatformView.SetMediaPlayer(null);
        }
    }

    protected override void DisconnectHandler(MediaPlayerElement platformView)
    {
        _player?.Pause();
        _player?.Dispose();
        _player = null;
        base.DisconnectHandler(platformView);
    }
}
