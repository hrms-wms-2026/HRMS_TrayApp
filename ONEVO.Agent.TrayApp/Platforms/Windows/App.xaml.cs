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
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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
