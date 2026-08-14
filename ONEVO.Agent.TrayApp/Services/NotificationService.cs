namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

/// <summary>
/// Simple one-way informational/warning Windows notifications. Unrelated to the actionable
/// Allow/Skip inactivity prompt — see <see cref="WindowsInactivityPromptService"/> for that flow.
/// These notifications carry no arguments and are never awaited for a decision.
/// </summary>
public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowInfo(string title, string message)
    {
        _logger.LogInformation("Notification: {Title} — {Message}", title, message);
        Show(title, message);
    }

    public void ShowWarning(string title, string message)
    {
        _logger.LogWarning("Notification: {Title} — {Message}", title, message);
        Show(title, message);
    }

    private void Show(string title, string message)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows notification: {Title}", title);
        }
    }
}
