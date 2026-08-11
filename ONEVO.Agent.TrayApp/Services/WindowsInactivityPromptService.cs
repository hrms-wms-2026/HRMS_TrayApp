namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

/// <summary>
/// Windows App SDK-backed <see cref="IInactivityPromptService"/>. Shows an actionable "Activity
/// check" toast with Allow/Skip buttons and resolves once the employee responds, the prompt
/// expires, or the caller cancels.
/// </summary>
/// <remarks>
/// Subscribes to <see cref="AppNotificationManager.NotificationInvoked"/> once, in the
/// constructor, and forwards raw activation argument strings to a <see cref="NotificationActivationRouter"/>
/// — the router itself has no Windows App SDK dependency and is unit-tested independently. The
/// <c>AppNotificationManager.Default.Register()</c>/<c>Unregister()</c> lifecycle calls happen
/// once at process scope in <c>Platforms/Windows/App.xaml.cs</c>, not here; this class only needs
/// the manager to already be registered by the time <see cref="PromptAsync"/> is first called,
/// which app startup guarantees.
/// </remarks>
public sealed class WindowsInactivityPromptService : IInactivityPromptService
{
    private const string NotificationTitle = "Activity check";
    private const string NotificationBody =
        "No keyboard or mouse activity was detected for 5 minutes. Allow a screenshot of all connected monitors?";

    private readonly NotificationActivationRouter _router;
    private readonly ILogger<WindowsInactivityPromptService> _logger;

    public WindowsInactivityPromptService(
        NotificationActivationRouter router,
        ILogger<WindowsInactivityPromptService> logger)
    {
        _router = router;
        _logger = logger;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to AppNotificationManager.NotificationInvoked");
        }
    }

    public async Task<InactivityPromptDecision> PromptAsync(
        Guid attemptId,
        TimeSpan idleFor,
        TimeSpan expiresIn,
        CancellationToken ct)
    {
        try
        {
            Show(attemptId, expiresIn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show inactivity prompt notification for attempt {AttemptId}", attemptId);
        }

        using var timeoutCts = new CancellationTokenSource(expiresIn);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await _router.WaitAsync(attemptId, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The expiry timer fired, not the caller's token — this is a normal, non-exceptional
            // outcome for the caller, so it is returned as a decision rather than rethrown.
            return InactivityPromptDecision.TimedOut;
        }
        finally
        {
            Dismiss(attemptId);
        }
    }

    public void Dismiss(Guid attemptId)
    {
        // Fire-and-forget, but observed: a failure here must never surface as an unobserved task
        // exception, and must never throw back into a caller that is just trying to clean up.
        _ = DismissAsync(attemptId);
    }

    private async Task DismissAsync(Guid attemptId)
    {
        try
        {
            await AppNotificationManager.Default.RemoveByTagAsync(attemptId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dismiss inactivity prompt notification for attempt {AttemptId}", attemptId);
        }
    }

    private static void Show(Guid attemptId, TimeSpan expiresIn)
    {
        var attempt = attemptId.ToString();

        var notification = new AppNotificationBuilder()
            .AddText(NotificationTitle)
            .AddText(NotificationBody)
            .AddButton(new AppNotificationButton("Allow")
                .AddArgument("attempt", attempt)
                .AddArgument("decision", "allow"))
            .AddButton(new AppNotificationButton("Skip")
                .AddArgument("attempt", attempt)
                .AddArgument("decision", "skip"))
            .BuildNotification();

        // Tag by attempt id so Dismiss(attemptId) — including the unconditional Dismiss(attemptId)
        // in PromptAsync's finally block — can only ever remove exactly this attempt's
        // notification, never a different (e.g. later/concurrent) attempt's: every attempt gets
        // its own Guid, so tags never collide across attempts. The builder is never given a
        // root-level AddArgument, so clicking the notification body (as opposed to a button)
        // carries no attempt/decision pair and the router safely ignores it — see
        // NotificationActivationRouter.Route.
        notification.Tag = attempt;
        notification.Expiration = DateTimeOffset.UtcNow.Add(expiresIn);

        AppNotificationManager.Default.Show(notification);
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Intentionally does nothing beyond routing: no Window.Activate(), no Shell navigation, no
        // foregrounding of the MAUI window. That is how this satisfies "do not activate or
        // foreground the MAUI window" for both the Allow and Skip buttons.
        _router.Route(args.Argument);
    }
}
