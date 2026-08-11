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
        builder.Services.AddSingleton<ScreenshotCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<ScreenshotCollector>());
        builder.Services.AddSingleton<CollectorCoordinator>();

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

        var app = builder.Build();

        // Force IInactivityPromptService to construct now, at startup, rather than lazily on the
        // first inactivity prompt — its constructor subscribes to
        // AppNotificationManager.NotificationInvoked, and that subscription must happen once,
        // during app startup, per the actionable-notification design.
        app.Services.GetRequiredService<IInactivityPromptService>();

        return app;
    }
}
