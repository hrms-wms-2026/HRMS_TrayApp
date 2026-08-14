namespace ONEVO.Agent.TrayApp.WinUI;

using Microsoft.Windows.AppNotifications;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();

        try
        {
            AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            BootLog($"AppNotificationManager.Register failed: {ex}");
        }

        // ProcessExit fires on normal shutdown regardless of which path triggered it (tray "Exit",
        // Windows sign-off, etc.), so this is a self-contained, App-instance-independent place to
        // undo Register() above without needing a hook into the shared MAUI window's lifecycle.
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
