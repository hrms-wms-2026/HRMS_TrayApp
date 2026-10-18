namespace ONEVO.Agent.TrayApp.Services;

using System.Text.Json;
using ONEVO.Agent.Shared.IPC;

/// <summary>Shows one generic notification toast. Returns false if Windows refused it.</summary>
public interface IGenericNotificationToast
{
    bool Show(GenericNotificationPushPayload payload, string activationArguments);
}

/// <summary>
/// Shows generic notification deliveries forwarded by the service and confirms each one back
/// (GenericNotificationAccepted), which is what lets the service acknowledge it to the backend.
/// Display is at-least-once: a toast shown just before a crash is re-offered and then recognised here by id.
/// </summary>
public sealed class GenericNotificationPresenter
{
    private readonly INamedPipeClient _pipe;
    private readonly IGenericNotificationToast _toast;
    private readonly ShownNotificationLog _shown;
    private readonly Func<DateTimeOffset> _clock;

    public GenericNotificationPresenter(
        INamedPipeClient pipe, IGenericNotificationToast toast, ShownNotificationLog shown, Func<DateTimeOffset>? clock = null)
    {
        _pipe = pipe;
        _toast = toast;
        _shown = shown;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _pipe.OnGenericNotificationReceived += payload => _ = HandleAsync(payload, CancellationToken.None);
    }

    public async Task HandleAsync(GenericNotificationPushPayload payload, CancellationToken ct)
    {
        try
        {
            // Too late to be useful; the backend marks it expired rather than accepted.
            if (payload.ExpiresAt is { } expiresAt && expiresAt <= _clock())
                return;

            if (!_shown.Contains(payload.DeliveryId))
            {
                if (!_toast.Show(payload, GenericNotificationActivation.BuildArguments(payload)))
                    return; // not shown - leave it pending so the service offers it again
                _shown.Add(payload.DeliveryId);
            }

            await _pipe.SendEnvelopeAsync(new IpcEnvelope
            {
                Type = IpcMessageTypes.GenericNotificationAccepted,
                Payload = JsonSerializer.SerializeToElement(new GenericNotificationAcceptedPayload(payload.DeliveryId))
            }, ct);
        }
        catch (Exception)
        {
            // Runs from a pipe callback: never throw. An unconfirmed delivery is simply re-offered later.
        }
    }
}
