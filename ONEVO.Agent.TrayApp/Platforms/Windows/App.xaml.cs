namespace ONEVO.Agent.TrayApp.WinUI;

using Microsoft.Windows.AppNotifications;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
        AppNotificationManager.Default.Register();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
