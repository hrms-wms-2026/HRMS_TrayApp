namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Outcome of an inactivity Allow/Skip prompt shown for one attempt.
/// </summary>
public enum InactivityPromptDecision
{
    /// <summary>The employee selected Allow — a screenshot capture may proceed.</summary>
    Allowed,

    /// <summary>The employee selected Skip — no capture.</summary>
    Declined,

    /// <summary>The prompt's <c>expiresIn</c> window elapsed with no employee response.</summary>
    TimedOut,

    /// <summary>Keyboard/mouse activity resumed before the employee responded.</summary>
    ActivityResumed,

    /// <summary>Monitoring stopped (break, clock-out, IPC loss, policy change, …) before the employee responded.</summary>
    MonitoringStopped
}

/// <summary>
/// Shows an actionable "Activity check" Windows notification for one inactivity attempt and
/// resolves once the employee responds, the prompt expires, or the caller cancels.
/// </summary>
/// <remarks>
/// <para>
/// Expiry is this service's responsibility, not the underlying activation router's: callers pass a
/// duration (<c>expiresIn</c>), not a pre-built token, so the service is the only place that can
/// turn "270 seconds" into a concrete deadline.
/// </para>
/// <para>
/// <see cref="Dismiss"/> only removes the on-screen/Action-Center notification for a given attempt;
/// it does not resolve or cancel a pending <see cref="PromptAsync"/> call. To abort an in-flight
/// prompt (activity resumed, monitoring stopped, …) the caller must cancel the
/// <see cref="CancellationToken"/> it originally passed to <see cref="PromptAsync"/> — that throws
/// <see cref="OperationCanceledException"/>, which the caller can catch and map to
/// <see cref="InactivityPromptDecision.ActivityResumed"/> or
/// <see cref="InactivityPromptDecision.MonitoringStopped"/> as appropriate. Calling
/// <see cref="Dismiss"/> alongside that cancellation hides the toast immediately instead of leaving
/// it on screen until the OS-driven expiration removes it.
/// </para>
/// </remarks>
public interface IInactivityPromptService
{
    /// <summary>
    /// Shows the Allow/Skip notification for <paramref name="attemptId"/> and waits for a
    /// decision. Resolves to <see cref="InactivityPromptDecision.Allowed"/> or
    /// <see cref="InactivityPromptDecision.Declined"/> when the employee responds, or
    /// <see cref="InactivityPromptDecision.TimedOut"/> when <paramref name="expiresIn"/> elapses
    /// first. Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> is
    /// cancelled before either happens.
    /// </summary>
    /// <param name="attemptId">Correlates this prompt to the Allow/Skip button activation.</param>
    /// <param name="idleFor">How long the employee has been idle, for display purposes.</param>
    /// <param name="expiresIn">How long the employee has to respond before the prompt times out.</param>
    /// <param name="ct">Cancelled by the caller to abort the prompt early (e.g. activity resumed).</param>
    Task<InactivityPromptDecision> PromptAsync(
        Guid attemptId,
        TimeSpan idleFor,
        TimeSpan expiresIn,
        CancellationToken ct);

    /// <summary>Removes the on-screen notification for <paramref name="attemptId"/>, if present. Safe no-op otherwise.</summary>
    void Dismiss(Guid attemptId);
}
