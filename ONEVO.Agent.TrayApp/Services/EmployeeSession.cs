namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.Models;

/// <summary>Reads the currently activated employee from session preferences.</summary>
public static class EmployeeSession
{
    public static string Name(IPreferencesStore prefs, string fallback = "") =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.EmployeeDisplayName, string.Empty), fallback);

    public static string Email(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.EmployeeEmail, string.Empty), string.Empty);

    public static string Id(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.EmployeeId, string.Empty), string.Empty);

    public static string Department(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.Department, string.Empty), string.Empty);

    public static string Office(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.OfficeName, string.Empty), string.Empty);

    public static string WorkMode(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.WorkMode, string.Empty), string.Empty);

    public static string Organization(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.Organization, string.Empty), string.Empty);

    public static string DeviceName(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.DeviceName, Environment.MachineName), Environment.MachineName);

    /// <summary>
    /// The daily-choice picker (WorkLocationViewModel) is the only place that saves
    /// WorkLocationDisplay, so a fixed work mode (AllowsDailyLocationChoice = false) - which skips
    /// that picker entirely - never populates it. For that case, derive the display from the
    /// policy's own fixed behavior instead of showing an empty dash: SelfRegistersLocation means
    /// their location is self-registered ("Work From Home" per the Clock-in Policy label),
    /// otherwise it's checked against the legal entity's office point ("Office").
    /// </summary>
    public static string WorkLocation(IPreferencesStore prefs, AgentPolicy? policy = null) =>
        ResolveWorkLocationDisplay(prefs.Get(SessionPreferenceKeys.WorkLocationDisplay, string.Empty), policy);

    public static string ResolveWorkLocationDisplay(string? savedDisplay, AgentPolicy? policy)
    {
        var saved = FirstNonEmpty(savedDisplay, string.Empty);
        if (!string.IsNullOrEmpty(saved))
            return saved;

        if (policy is not null && !policy.AllowsDailyLocationChoice)
            return policy.SelfRegistersLocation ? "Work From Home" : "Office";

        return "—";
    }

    public static string FirstNonEmpty(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
