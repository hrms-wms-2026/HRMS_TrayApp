namespace ONEVO.Agent.TrayApp.Collectors;

/// <summary>
/// Reports continuous keyboard/mouse inactivity for the interactive Windows session (§7.3).
/// Isolates the Win32 <c>GetLastInputInfo</c> call so <see cref="InactivityScreenshotCollector"/>'s
/// bucket state machine (<see cref="InactivityScreenshotCollector.EvaluateAsync"/>) can be
/// unit-tested by passing idle seconds directly, with no Windows API dependency.
/// </summary>
public interface IIdleTimeProvider
{
    /// <summary>Whole seconds of continuous keyboard/mouse inactivity on this interactive session.</summary>
    int GetIdleSeconds();
}
