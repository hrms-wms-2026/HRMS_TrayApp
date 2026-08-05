namespace ONEVO.Agent.TrayApp;

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

        builder.Services.AddTransient<LoginWindowViewModel>();
        builder.Services.AddTransient<StatusPopupViewModel>();
        builder.Services.AddTransient<PhotoCaptureWindowViewModel>();

        builder.Logging.AddDebug();

        return builder.Build();
    }
}
