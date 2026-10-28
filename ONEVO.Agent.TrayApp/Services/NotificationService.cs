namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

/// <summary>
/// Simple one-way informational/warning Windows notifications. Unrelated to the actionable
/// Allow/Skip inactivity prompt — see <see cref="WindowsInactivityPromptService"/> for that flow.
/// These notifications carry no arguments and are never awaited for a decision.
/// </summary>
public sealed class NotificationService : IGenericNotificationToast
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowInfo(string title, string message)
    {
        // Title/message text can carry names or other personal details; never write it to tray.log.
        _logger.LogInformation("Info notification shown");
        Show(title, message);
    }

    public void ShowWarning(string title, string message)
    {
        _logger.LogWarning("Warning notification shown");
        Show(title, message);
    }

    /// <summary>A generic notification toast carrying click arguments (see GenericNotificationActivation).</summary>
    public bool Show(ONEVO.Agent.Shared.IPC.GenericNotificationPushPayload payload, string activationArguments)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("kind", "generic")
                .AddArgument("delivery", payload.DeliveryId.ToString())
                .AddArgument("action", payload.ActionKey)
                .AddText(payload.Title)
                .AddText(payload.Message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            _logger.LogInformation("Generic notification shown: {EventKey} {DeliveryId}", payload.EventKey, payload.DeliveryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show generic notification {DeliveryId}", payload.DeliveryId);
            return false;
        }
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
            _logger.LogWarning(ex, "Failed to show Windows notification");
        }
    }
}
