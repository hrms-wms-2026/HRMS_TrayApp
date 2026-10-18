namespace ONEVO.Agent.Service.Sync;

using System.Text.Json;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Polls for pending break/idle wellness notifications and broadcasts them to connected
/// Tray clients over IPC — the same push mechanism PolicySyncService already uses for
/// PolicyPush, not the stubbed SignalR hub (AgentCommandListener).
/// </summary>
public sealed class NotificationPollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    // While the backend can push (NotificationsPending), this is only a safety net.
    internal static readonly TimeSpan PushFallbackInterval = TimeSpan.FromMinutes(15);

    private readonly ILogger<NotificationPollingService> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;
    private readonly IIpcBroadcaster _broadcaster;
    private readonly PushChannelState _push;
    private readonly AgentStateMachine? _stateMachine;

    public NotificationPollingService(
        ILogger<NotificationPollingService> logger,
        OnevoApiClient apiClient,
        CredentialStore credentials,
        IIpcBroadcaster broadcaster,
        PushChannelState? push = null,
        AgentStateMachine? stateMachine = null)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
        _broadcaster = broadcaster;
        _push = push ?? new PushChannelState();
        _stateMachine = stateMachine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // "Break allowance exceeded" is raised server-side by the attendance poll, not pushed,
            // so stay on the fast cadence while on break.
            await _push.RunAdaptiveLoopAsync(
                PollInterval, PushFallbackInterval, SafePollAsync, stoppingToken,
                forceFast: () => _stateMachine?.CurrentState == MonitoringState.Paused);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Never throws except on shutdown — also called from push handlers.</summary>
    public async Task SafePollAsync(CancellationToken ct)
    {
        try
        {
            await PollOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Notification poll failed — will retry next cycle");
        }
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

        // Generic deliveries are acknowledged only after the tray process confirms it accepted the toast
        // (GenericNotificationAccepted → AgentWorker), so a closed or crashed tray never loses one: it stays
        // pending server-side and is offered again here. The tray suppresses repeats by delivery id.
        var deliveries = await _apiClient.GetPendingTrayDeliveriesAsync(jwt, ct);
        foreach (var delivery in deliveries)
        {
            await _broadcaster.BroadcastAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.GenericNotificationPush,
                Payload = JsonSerializer.SerializeToElement(new GenericNotificationPushPayload
                {
                    DeliveryId = delivery.DeliveryId,
                    EventKey = delivery.EventKey,
                    Title = delivery.Title,
                    Message = delivery.Message,
                    ActionKey = delivery.ActionKey,
                    RelatedEntityType = delivery.RelatedEntityType,
                    RelatedEntityId = delivery.RelatedEntityId,
                    ExpiresAt = delivery.ExpiresAt
                })
            }, ct);
        }

        if (deliveries.Count > 0)
            _logger.LogInformation("Forwarded {Count} notification delivery(ies) to tray", deliveries.Count);
    }
}
