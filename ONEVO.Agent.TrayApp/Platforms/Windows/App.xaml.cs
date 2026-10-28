namespace ONEVO.Agent.TrayApp.WinUI;

using Microsoft.Windows.AppNotifications;
using ONEVO.Agent.TrayApp.Services.Diagnostics;

public partial class App : MauiWinUIApplication
{
    // Held for the process lifetime; Windows releases the mutex when the process exits.
    private static ONEVO.Agent.TrayApp.Services.SingleInstanceGuard? _singleInstance;

    public App()
    {
        TrayBootLog.RollFallbackIfLarge();

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
            // Logging exists from here on: every later trace line goes to tray.log, not tray-boot.log.
            TrayBootLog.Attach(app.Services.GetRequiredService<ILoggerFactory>());
            BootLog("CreateMauiApp exit ok");

            // Most MAUI-on-Windows crashes surface on the XAML dispatcher, not AppDomain, so
            // record them in tray.log too. Handled stays false: the crash behaviour is unchanged.
            var crashLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(ONEVO.Agent.TrayApp.Services.Diagnostics.TrayLogging.CrashCategory);
            this.UnhandledException += (_, e) =>
            {
                BootLog($"XAML UnhandledException: {e.Exception}");
                ONEVO.Agent.TrayApp.Services.Diagnostics.TrayLogging.LogCrash(
                    crashLogger, "XamlDispatcher", e.Exception, isTerminating: !e.Handled);
            };

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

    private static void BootLog(string message) => TrayBootLog.Write("Startup", message);
}
