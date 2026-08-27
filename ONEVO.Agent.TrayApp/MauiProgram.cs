namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;
using ONEVO.Agent.TrayApp.Views;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        DisableMauiAspireIntegration();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureMauiHandlers(h =>
        {
            h.AddHandler<Controls.CameraPreview, Platforms.Windows.CameraPreviewHandler>();
            h.AddHandler<Controls.BiometricWebView, Platforms.Windows.BiometricWebViewHandler>();
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
        builder.Services.AddSingleton<ISessionDayMetrics, SessionDayMetrics>();
        builder.Services.AddSingleton<IAppIconCache, AppIconCache>();
        builder.Services.AddSingleton<CapturedPhotoBuffer>();

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
        builder.Services.AddTransient<ReviewSetupViewModel>();
        builder.Services.AddTransient<PrivacyConsentViewModel>();
        builder.Services.AddTransient<ClockInViewModel>();
        builder.Services.AddTransient<ActiveSessionViewModel>();
        builder.Services.AddTransient<EndSessionViewModel>();
        builder.Services.AddTransient<StatusPopupViewModel>();
        builder.Services.AddTransient<PhotoCaptureWindowViewModel>();
        builder.Services.AddTransient<BiometricEnrollmentViewModel>();
        builder.Services.AddTransient<IdentityVerificationViewModel>();

        // Views
        builder.Services.AddTransient<ConnectWorkspacePage>();
        builder.Services.AddTransient<PrepareWorkspacePage>();
        builder.Services.AddTransient<ReviewSetupPage>();
        builder.Services.AddTransient<PrivacyConsentPage>();
        builder.Services.AddTransient<ClockInPage>();
        builder.Services.AddTransient<ActiveSessionPage>();
        builder.Services.AddTransient<EndSessionPage>();
        builder.Services.AddTransient<PhotoCaptureWindow>();
        builder.Services.AddTransient<BiometricEnrollmentPage>();
        builder.Services.AddTransient<IdentityVerificationPage>();

        var app = builder.Build();

        // Force IInactivityPromptService to construct now, at startup, rather than lazily on the
        // first inactivity prompt — its constructor subscribes to
        // AppNotificationManager.NotificationInvoked, and that subscription must happen once,
        // during app startup, per the actionable-notification design.
        app.Services.GetRequiredService<IInactivityPromptService>();

        return app;
    }

    /// <summary>
    /// TrayApp is a standalone Windows tray app — never orchestrated by .NET Aspire. MAUI's
    /// MauiApp.CreateBuilder() calls ConfigureEnvironmentVariables(), which — when this switch is
    /// left at its default of true — reads both ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT (its
    /// Aspire-support code strips the "ASPNETCORE_"/"DOTNET_" prefix from each), collapsing both to
    /// the same bare config key "ENVIRONMENT". Whenever a launcher sets both (e.g.
    /// scripts/run-all.ps1 sets ASPNETCORE_ENVIRONMENT for the backend and DOTNET_ENVIRONMENT for
    /// the Agent Service, and TrayApp inherits both from that same shell), the second insert throws
    /// ArgumentException and the whole app fails to boot before any collector starts — confirmed via
    /// tray-boot.log crashes spanning 2026-08-18 to 2026-08-21 and reproduced live. Since Aspire
    /// integration is irrelevant to this app, disable it outright.
    /// </summary>
    internal static void DisableMauiAspireIntegration() =>
        AppContext.SetSwitch("Microsoft.Maui.RuntimeFeature.EnableMauiAspire", false);
}
