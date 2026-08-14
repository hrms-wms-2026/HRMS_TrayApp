namespace ONEVO.Agent.TrayApp.Services;

using System.Drawing;
using System.Windows.Forms;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// System tray icon for monitoring status.
/// Uses the main UI thread message pump (no second WinForms Application.Run)
/// to avoid COM/XAML apartment crashes with MAUI WinUI.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private NotifyIcon? _notifyIcon;
    private bool _initialized;
    private Icon? _appIcon;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
    }

    private Icon AppIcon
    {
        get
        {
            if (_appIcon is not null)
                return _appIcon;

            try
            {
                var assembly = typeof(TrayIconService).Assembly;
                using var stream = assembly.GetManifestResourceStream("ONEVO.Agent.TrayApp.app_tray.ico");
                _appIcon = stream is not null ? new Icon(stream) : SystemIcons.Application;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load embedded tray icon; falling back to system icon");
                _appIcon = SystemIcons.Application;
            }

            return _appIcon;
        }
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        try
        {
            // Prefer main-thread init when called from MAUI window lifecycle.
            if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
                CreateIcon();
            else
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(CreateIcon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray icon initialize failed");
        }
    }

    private void CreateIcon()
    {
        if (_initialized)
            return;

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "OneXso WorkPulse — Starting...",
            Icon = AppIcon,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OnTrayDoubleClick();
        _initialized = true;
        _logger.LogInformation("Tray icon initialized");
    }

    public void UpdateState(MonitoringState state)
    {
        void Apply()
        {
            if (_notifyIcon is null)
                return;

            _notifyIcon.Text = $"OneXso WorkPulse — {state switch
            {
                MonitoringState.Active     => "Monitoring Active",
                MonitoringState.Paused     => "On Break",
                MonitoringState.Stopped    => "Ready",
                MonitoringState.Locked     => "Locked — Action Required",
                MonitoringState.Unenrolled => "Setup Required",
                _                          => state.ToString()
            }}";

            _notifyIcon.Icon = AppIcon;
        }

        try
        {
            if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
                Apply();
            else
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(Apply);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tray UpdateState failed");
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ONEVO", null, (_, _) => OnTrayDoubleClick());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            try
            {
                if (Microsoft.Maui.Controls.Application.Current is App app)
                    app.RequestExit();
                else
                    Microsoft.Maui.Controls.Application.Current?.Quit();
            }
            catch
            {
                Environment.Exit(0);
            }
        });
        return menu;
    }

    private static void OnTrayDoubleClick()
    {
#if WINDOWS
        try
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0)
                {
                    var mauiWindow = Microsoft.Maui.Controls.Application.Current.Windows[0];
                    if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
                        && native.AppWindow is not null)
                    {
                        native.AppWindow.Show();
                        native.Activate();
                    }
                }
            });
        }
        catch { /* ignore */ }
#endif
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _appIcon?.Dispose();
        _appIcon = null;
        _initialized = false;
    }
}
