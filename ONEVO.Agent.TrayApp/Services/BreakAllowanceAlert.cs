namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Set when the active session already showed the break-allowance toast, so the
/// later polled notification does not show a second one.
/// </summary>
public static class BreakAllowanceAlert
{
    public static bool Shown { get; set; }
}
