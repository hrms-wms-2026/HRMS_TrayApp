namespace ONEVO.Agent.TrayApp.Services;

using System.Drawing;
using System.Windows.Forms;
using ONEVO.Agent.Shared.Models;

public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private NotifyIcon? _notifyIcon;
    private Thread? _thread;
    private readonly TaskCompletionSource _ready = new();

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        _thread = new Thread(RunMessagePump) { IsBackground = true, Name = "TrayIconThread" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.Wait(TimeSpan.FromSeconds(5));
        _logger.LogInformation("Tray icon initialized");
    }

    private void RunMessagePump()
    {
        System.Windows.Forms.Application.EnableVisualStyles();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "ONEVO WorkPulse — Starting...",
            Icon = SystemIcons.Application,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OnTrayDoubleClick();
        _ready.SetResult();
        System.Windows.Forms.Application.Run();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    public void UpdateState(MonitoringState state)
    {
        if (_notifyIcon is null) return;

        void Apply()
        {
            _notifyIcon.Text = $"ONEVO WorkPulse — {state switch
            {
                MonitoringState.Active     => "Monitoring Active",
                MonitoringState.Paused     => "On Break",
                MonitoringState.Stopped    => "Ready",
                MonitoringState.Locked     => "Locked — Action Required",
                MonitoringState.Unenrolled => "Setup Required",
                _                          => state.ToString()
            }}";

            _notifyIcon.Icon = state switch
            {
                MonitoringState.Active => SystemIcons.Information,
                MonitoringState.Locked => SystemIcons.Error,
                _                      => SystemIcons.Application
            };
        }

        // NotifyIcon is owned by the STA tray thread — marshal via ContextMenuStrip handle if available.
        var menu = _notifyIcon.ContextMenuStrip;
        if (menu is not null && menu.IsHandleCreated && menu.InvokeRequired)
            menu.BeginInvoke(Apply);
        else
            Apply();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ONEVO", null, (_, _) => OnTrayDoubleClick());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            System.Windows.Forms.Application.ExitThread();
            Microsoft.Maui.Controls.Application.Current?.Quit();
        });
        return menu;
    }

    private static void OnTrayDoubleClick()
    {
        // Enrollment/status window wired in Phase 2
    }

    public void Dispose()
    {
        System.Windows.Forms.Application.ExitThread();
    }
}
