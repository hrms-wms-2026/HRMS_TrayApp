namespace ONEVO.Agent.TrayApp.WinUI;

using Microsoft.Windows.AppNotifications;

public partial class App : MauiWinUIApplication
{
    // Held for the process lifetime; Windows releases the mutex when the process exits.
    private static ONEVO.Agent.TrayApp.Services.SingleInstanceGuard? _singleInstance;

    public App()
    {
        RollBootLogIfLarge();

        _singleInstance = ONEVO.Agent.TrayApp.Services.SingleInstanceGuard.TryAcquire();
        if (_singleInstance is null)
        {
            BootLog("Another tray instance is already running — asked it to show its window, exiting");
            Environment.Exit(0);
        }
        _singleInstance.OnActivationRequested(ONEVO.Agent.TrayApp.Services.TrayIconService.ShowMainWindow);

        this.InitializeComponent();

        // ProcessExit fires on normal shutdown regardless of which path triggered it (tray "Exit",
        // Windows sign-off, etc.), so this is a self-contained, App-instance-independent place to
        // undo Register() (in CreateMauiApp below) without needing a hook into the shared MAUI
        // window's lifecycle.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                BootLog($"AppNotificationManager.Unregister failed: {ex}");
            }
        };
    }

    protected override MauiApp CreateMauiApp()
    {
        try
        {
            BootLog("CreateMauiApp enter");
            var app = MauiProgram.CreateMauiApp();
            BootLog("CreateMauiApp exit ok");

            // Register() must run AFTER MauiProgram.CreateMauiApp() above, not before: that call
            // eagerly constructs IInactivityPromptService, whose constructor subscribes to
            // AppNotificationManager.NotificationInvoked. Windows App SDK requires every
            // NotificationInvoked handler to be subscribed BEFORE Register() is called — calling
            // Register() first (as this used to, in the constructor) makes the later Subscribe
            // throw ("Must register event handlers before calling Register()"), which was silently
            // swallowed, so the click-routing handler was never actually wired up.
            try
            {
                AppNotificationManager.Default.Register();
                BootLog("AppNotificationManager.Register OK");
            }
            catch (Exception ex)
            {
                BootLog($"AppNotificationManager.Register failed: {ex}");
            }

            return app;
        }
        catch (Exception ex)
        {
            BootLog($"CreateMauiApp FAILED: {ex}");
            throw;
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            BootLog("OnLaunched enter");
            base.OnLaunched(args);
            BootLog("OnLaunched exit ok");
        }
        catch (Exception ex)
        {
            BootLog($"OnLaunched FAILED: {ex}");
            throw;
        }
    }

    private static string BootLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    /// <summary>
    /// Every component appends to tray-boot.log and nothing ever trimmed it (it reached several MB
    /// on a dev box). Roll it to .old once per launch when it passes 2 MB so it stays bounded.
    /// </summary>
    private static void RollBootLogIfLarge()
    {
        try
        {
            var log = new System.IO.FileInfo(BootLogPath);
            if (log.Exists && log.Length > 2 * 1024 * 1024)
                log.MoveTo(BootLogPath + ".old", overwrite: true);
        }
        catch
        {
            // Locked or missing — keep appending to the current file.
        }
    }

    private static void BootLog(string message)
    {
        try
        {
            var path = BootLogPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore — this IS the fallback diagnostic path, nothing further to fall back to
        }
    }
}
