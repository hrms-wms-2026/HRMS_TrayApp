namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Whether the signed-in user's Windows session is currently usable (unlocked, at the console).
/// Interactive collectors must stop while it is locked, signed off or switched away (AUD-014).
/// </summary>
public interface IInteractiveSessionState
{
    bool IsLocked { get; }

    /// <summary>Raised with the new locked state when the session is locked or unlocked.</summary>
    event Action<bool>? LockChanged;
}
