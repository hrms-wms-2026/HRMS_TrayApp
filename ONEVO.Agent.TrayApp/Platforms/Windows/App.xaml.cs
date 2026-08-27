namespace ONEVO.Agent.TrayApp.WinUI;

using Microsoft.Windows.AppNotifications;

public partial class App : MauiWinUIApplication
{
    public App()
    {
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

    private static void BootLog(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ONEVO", "Agent", "tray-boot.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore — this IS the fallback diagnostic path, nothing further to fall back to
        }
    }
}
