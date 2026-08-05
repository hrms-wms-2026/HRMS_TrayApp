namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<NotificationService>();

        // Interactive collectors (Tray session only — never Service Session 0)
        builder.Services.AddSingleton<ActivityCountCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<ActivityCountCollector>());
        builder.Services.AddSingleton<CollectorCoordinator>();

        builder.Services.AddTransient<LoginWindowViewModel>();
        builder.Services.AddTransient<StatusPopupViewModel>();
        builder.Services.AddTransient<PhotoCaptureWindowViewModel>();

        return builder.Build();
    }
}
