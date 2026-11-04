namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Toast activation arguments for the "still clocked in after 24 hours" reminder
/// (<c>kind=longsession;action={clockout|portal}</c>, the same semicolon-separated shape Windows App SDK
/// produces for the other toasts). Parsing never throws: it runs on the NotificationInvoked callback.
/// </summary>
public static class LongSessionActivation
{
    public const string KindValue = "longsession";

    /// <summary>Open the tray window on the clock-out confirmation.</summary>
    public const string ClockOut = "clockout";

    /// <summary>Open the portal dashboard (the tray cannot clock out when tray clock-in is off).</summary>
    public const string OpenPortal = "portal";

    private const string KindPair = "kind=" + KindValue;

    public static bool IsLongSession(string? args)
        => !string.IsNullOrWhiteSpace(args)
           && args.Split(';', StringSplitOptions.RemoveEmptyEntries).Contains(KindPair, StringComparer.Ordinal);

    /// <summary>The clicked action, or null for a malformed or unknown one.</summary>
    public static string? TryGetAction(string? args)
    {
        if (!IsLongSession(args))
            return null;

        foreach (var pair in args!.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "action")
                return parts[1] is ClockOut or OpenPortal ? parts[1] : null;
        }

        return null;
    }
}
