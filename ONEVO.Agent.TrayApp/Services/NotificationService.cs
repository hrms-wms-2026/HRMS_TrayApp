namespace ONEVO.Agent.TrayApp.Services;

public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowInfo(string title, string message) =>
        _logger.LogInformation("Notification: {Title} — {Message}", title, message);

    public void ShowWarning(string title, string message) =>
        _logger.LogWarning("Notification: {Title} — {Message}", title, message);
}
