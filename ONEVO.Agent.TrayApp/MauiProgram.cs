namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;
using ONEVO.Agent.TrayApp.Views;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureMauiHandlers(h =>
        {
            h.AddHandler<Controls.CameraPreview, Platforms.Windows.CameraPreviewHandler>();
            h.AddHandler<Controls.BiometricWebView, Platforms.Windows.BiometricWebViewHandler>();

            // Shell wraps every page's content in a native ScrollViewer on Windows. Our pages
            // lay themselves out to fit the window, so that wrapper only causes unwanted
            // mouse-wheel scrolling of the whole page (including rows meant to stay fixed) —
            // find it in the visual tree and turn its scrolling off.
            Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("DisableShellRootScroll", (handler, _) =>
            {
                if (handler.PlatformView is not Microsoft.UI.Xaml.FrameworkElement platformView)
                    return;

                platformView.Loaded += (_, _) =>
                {
                    var node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(platformView);
                    while (node is not null)
                    {
                        if (node is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
                        {
                            scrollViewer.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
                            scrollViewer.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
                            scrollViewer.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
                            scrollViewer.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
                            break;
                        }
                        node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
                    }
                };
            });
        });

        // Infrastructure
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<INamedPipeClient>(sp =>
            sp.GetRequiredService<NamedPipeClient>());
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<NotificationActivationRouter>();
        builder.Services.AddSingleton<WindowsInactivityPromptService>();
        builder.Services.AddSingleton<IInactivityPromptService>(sp =>
            sp.GetRequiredService<WindowsInactivityPromptService>());
        builder.Services.AddSingleton<IPreferencesStore, PreferencesStore>();
        builder.Services.AddSingleton<ICameraService, CameraService>();
        builder.Services.AddSingleton<ILocationService, GeolocationService>();
        builder.Services.AddSingleton<ISessionDayMetrics, SessionDayMetrics>();
        builder.Services.AddSingleton<IAppIconCache, AppIconCache>();

        // Collectors
        builder.Services.AddSingleton<ActivityCountCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<ActivityCountCollector>());
        builder.Services.AddSingleton<AppUsageCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<AppUsageCollector>());
        builder.Services.AddSingleton<DeviceStateCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<DeviceStateCollector>());
        builder.Services.AddSingleton<MeetingDetector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<MeetingDetector>());
        builder.Services.AddSingleton<IIdleTimeProvider, WindowsIdleTimeProvider>();
        builder.Services.AddSingleton<Capture.IScreenshotCaptureService, Capture.VirtualDesktopScreenshotCaptureService>();
        builder.Services.AddSingleton<InactivityScreenshotCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<InactivityScreenshotCollector>());
        builder.Services.AddSingleton<CollectorCoordinator>();
        builder.Services.AddSingleton<ICollectorLifecycleCoordinator>(sp =>
            sp.GetRequiredService<CollectorCoordinator>());

        // ViewModels
        builder.Services.AddTransient<ConnectWorkspaceViewModel>();
        builder.Services.AddTransient<PrepareWorkspaceViewModel>();
        builder.Services.AddTransient<WorkLocationViewModel>();
        builder.Services.AddTransient<ReviewSetupViewModel>();
        builder.Services.AddTransient<PrivacyConsentViewModel>();
        builder.Services.AddTransient<ClockInViewModel>();
        builder.Services.AddTransient<ActiveSessionViewModel>();
        builder.Services.AddTransient<EndSessionViewModel>();
        builder.Services.AddTransient<StatusPopupViewModel>();
        builder.Services.AddTransient<PhotoCaptureWindowViewModel>();
        builder.Services.AddTransient<BiometricEnrollmentViewModel>();

        // Views
        builder.Services.AddTransient<ConnectWorkspacePage>();
        builder.Services.AddTransient<PrepareWorkspacePage>();
        builder.Services.AddTransient<WorkLocationPage>();
        builder.Services.AddTransient<ReviewSetupPage>();
        builder.Services.AddTransient<PrivacyConsentPage>();
        builder.Services.AddTransient<ClockInPage>();
        builder.Services.AddTransient<ActiveSessionPage>();
        builder.Services.AddTransient<EndSessionPage>();
        builder.Services.AddTransient<PhotoCaptureWindow>();
        builder.Services.AddTransient<BiometricEnrollmentPage>();

        var app = builder.Build();

        // Force IInactivityPromptService to construct now, at startup, rather than lazily on the
        // first inactivity prompt — its constructor subscribes to
        // AppNotificationManager.NotificationInvoked, and that subscription must happen once,
        // during app startup, per the actionable-notification design.
        app.Services.GetRequiredService<IInactivityPromptService>();

        return app;
    }
}
