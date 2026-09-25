namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// The Service rebroadcasts the monitoring state about once a minute. Routing on every copy
/// throws the employee off the face-capture screen mid-scan (a "Stopped" rebroadcast sends them
/// back to Clock In). Pages in the middle of a user-driven flow keep the screen until the state
/// genuinely changes.
/// </summary>
public static class StateNavigationGuard
{
    private static readonly string[] HeldRoutes = ["//photo"];

    public static bool ShouldHoldCurrentPage(
        string? currentLocation, MonitoringState? previousState, MonitoringState nextState)
    {
        if (previousState != nextState)
            return false;

        if (string.IsNullOrEmpty(currentLocation))
            return false;

        foreach (var route in HeldRoutes)
        {
            // Covers child routes too, e.g. //photo/identity-verification.
            if (currentLocation.StartsWith(route, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
