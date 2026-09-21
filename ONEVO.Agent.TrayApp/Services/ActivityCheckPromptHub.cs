namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Bridges the inactivity collector's prompt (any thread) to the Active Session overlay
/// so Allow/Skip is visible in the tray window, not only as a 5-second Windows banner.
/// </summary>
public sealed record ActivityCheckPrompt(Guid AttemptId, string Body);

public sealed class ActivityCheckPromptHub
{
    public event Action<ActivityCheckPrompt>? Shown;
    public event Action<Guid>? Closed;

    public void RaiseShown(ActivityCheckPrompt prompt) => Shown?.Invoke(prompt);

    public void RaiseClosed(Guid attemptId) => Closed?.Invoke(attemptId);
}
