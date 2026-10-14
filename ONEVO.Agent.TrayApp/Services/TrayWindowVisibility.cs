namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Whether the main window is actually on screen. Close-to-tray only hides the native window, so
/// MAUI never raises OnDisappearing — pages use this to stop UI timers, looping animations and the
/// camera preview while the app sits in the tray, and to resume them when it is shown again.
/// Raised on the UI thread.
/// </summary>
public static class TrayWindowVisibility
{
    public static bool IsVisible { get; private set; } = true;

    public static event Action<bool>? Changed;

    /// <summary>Returns true only when visibility actually flipped (moves/resizes are no-ops).</summary>
    internal static bool Set(bool isVisible)
    {
        if (IsVisible == isVisible)
            return false;

        IsVisible = isVisible;
        Changed?.Invoke(isVisible);
        return true;
    }
}
