namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Routes the tray through a location confirmation before setup or clock-in.
/// Location is captured once at the start of setup and again at the start of each workday.
/// </summary>
public static class WorkLocationFlow
{
    public const string PrepareRoute = "//prepare";
    public const string ClockInRoute = "//clockin";
    public const string LocationThenPrepare = "//location?next=prepare";
    public const string LocationThenClockIn = "//location?next=clockin";

    public static string TodayKey(DateTimeOffset? now = null) =>
        (now ?? DateTimeOffset.Now).ToString("yyyy-MM-dd");

    public static bool IsConfirmedToday(IPreferencesStore prefs, DateTimeOffset? now = null)
    {
        var saved = prefs.Get(SessionPreferenceKeys.WorkLocationConfirmedOn, string.Empty);
        return string.Equals(saved, TodayKey(now), StringComparison.Ordinal);
    }

    public static void MarkConfirmedToday(IPreferencesStore prefs, DateTimeOffset? now = null) =>
        prefs.Set(SessionPreferenceKeys.WorkLocationConfirmedOn, TodayKey(now));

    public static bool IsSetupComplete(IPreferencesStore prefs) =>
        string.Equals(
            prefs.Get(SessionPreferenceKeys.SetupCompleted, string.Empty),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static void MarkSetupComplete(IPreferencesStore prefs) =>
        prefs.Set(SessionPreferenceKeys.SetupCompleted, "true");

    public static string ResolveNextRoute(string? next) =>
        string.Equals(next, "clockin", StringComparison.OrdinalIgnoreCase)
            ? ClockInRoute
            : string.Equals(next, "policy", StringComparison.OrdinalIgnoreCase)
                ? SetupFlow.Permissions
                : string.Equals(next, "prepare", StringComparison.OrdinalIgnoreCase)
                    ? PrepareRoute
                    : SetupFlow.Privacy;

    /// <summary>
    /// Where an enrolled Stopped employee should land.
    /// Empty means first-time setup is still in progress — do not hijack the current page.
    /// </summary>
    public static string RouteWhenStopped(IPreferencesStore prefs, DateTimeOffset? now = null)
    {
        if (!IsSetupComplete(prefs))
            return string.Empty;

        return RouteToStartWork(prefs, now);
    }

    public static string RouteToStartWork(IPreferencesStore prefs, DateTimeOffset? now = null) =>
        IsConfirmedToday(prefs, now) ? SetupFlow.WelcomeBack : LocationThenClockIn;
}
