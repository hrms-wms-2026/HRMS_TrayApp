namespace ONEVO.Agent.Service.Sync;

using System.Text.Json;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;

/// <summary>
/// Polls for pending break/idle wellness notifications and broadcasts them to connected
/// Tray clients over IPC — the same push mechanism PolicySyncService already uses for
/// PolicyPush, not the stubbed SignalR hub (AgentCommandListener).
/// </summary>
public sealed class NotificationPollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<NotificationPollingService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly IIpcBroadcaster _broadcaster;

    public NotificationPollingService(
        ILogger<NotificationPollingService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        IIpcBroadcaster broadcaster)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _broadcaster = broadcaster;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notification poll failed — will retry next cycle");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PollOnceAsync(CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
            return;

        var pending = await _apiClient.GetPendingNotificationsAsync(jwt, ct);
        foreach (var notification in pending)
        {
            await _broadcaster.BroadcastAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.NotificationPush,
                Payload = JsonSerializer.SerializeToElement(new NotificationPushPayload
                {
                    NotificationId = notification.Id,
                    Type = notification.Type,
                    Title = notification.Title,
                    Message = notification.Message
                })
            }, ct);

            await _apiClient.AckNotificationAsync(jwt, notification.Id, ct);
        }

        if (pending.Count > 0)
            _logger.LogInformation("Delivered {Count} wellness notification(s) to tray", pending.Count);
    }
}
