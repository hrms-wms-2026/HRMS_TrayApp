namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;

/// <summary>
/// Correlates a Windows notification's Allow/Skip activation arguments (a plain
/// <c>attempt={guid}&amp;decision={allow|skip}</c> query string) back to the pending
/// <see cref="IInactivityPromptService.PromptAsync"/> call waiting on that attempt.
/// </summary>
/// <remarks>
/// Deliberately has no Windows App SDK dependency — it only parses a string and completes a
/// <see cref="TaskCompletionSource{TResult}"/>, which is what makes it unit-testable without a
/// real Windows notification runtime. <see cref="WindowsInactivityPromptService"/> is the
/// Windows-App-SDK-aware layer that builds the notification, subscribes to
/// <c>AppNotificationManager.NotificationInvoked</c>, and forwards the raw argument string here.
/// This class also owns no timer/expiry logic — see the note on
/// <see cref="IInactivityPromptService"/> for why that lives in the prompt service instead.
/// </remarks>
public sealed class NotificationActivationRouter
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<InactivityPromptDecision>> _pending = new();

    /// <summary>
    /// Registers a wait for <paramref name="attemptId"/>'s activation. Resolves when a matching
    /// <see cref="Route"/> call arrives, or observes cancellation if <paramref name="ct"/> fires
    /// first — including a token that is already cancelled when this is called.
    /// </summary>
    public Task<InactivityPromptDecision> WaitAsync(Guid attemptId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<InactivityPromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[attemptId] = tcs;

        if (ct.IsCancellationRequested)
        {
            _pending.TryRemove(new KeyValuePair<Guid, TaskCompletionSource<InactivityPromptDecision>>(attemptId, tcs));
            tcs.TrySetCanceled(ct);
            return tcs.Task;
        }

        var registration = ct.Register(static state =>
        {
            var (self, id, token, source) = ((NotificationActivationRouter Self, Guid Id, CancellationToken Token, TaskCompletionSource<InactivityPromptDecision> Source))state!;
            if (self._pending.TryRemove(new KeyValuePair<Guid, TaskCompletionSource<InactivityPromptDecision>>(id, source)))
                source.TrySetCanceled(token);
        }, (this, attemptId, ct, tcs));

        // Release the registration once the task settles by any means, so a long-lived router
        // does not accumulate CancellationTokenRegistration instances for completed attempts.
        tcs.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            registration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return tcs.Task;
    }

    /// <summary>
    /// Parses a notification activation argument string and completes the matching pending wait,
    /// if any. Unknown attempt ids, malformed strings, unrecognized decision values, and duplicate
    /// activations are all safe no-ops — this must never throw, since it runs on a Windows App SDK
    /// notification callback.
    /// </summary>
    public void Route(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return;

        Guid? attemptId = null;
        string? decisionText = null;

        foreach (var pair in args.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
                continue;

            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);

            if (key.Equals("attempt", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out var parsed))
                attemptId = parsed;
            else if (key.Equals("decision", StringComparison.OrdinalIgnoreCase))
                decisionText = value;
        }

        if (attemptId is not { } id)
            return;

        InactivityPromptDecision? decision = decisionText switch
        {
            "allow" => InactivityPromptDecision.Allowed,
            "skip" => InactivityPromptDecision.Declined,
            _ => null
        };

        if (decision is not { } resolvedDecision)
            return;

        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetResult(resolvedDecision);
    }
}
