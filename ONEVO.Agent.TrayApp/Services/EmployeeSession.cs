namespace ONEVO.Agent.TrayApp.Services;

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

    public static string WorkLocation(IPreferencesStore prefs) =>
        FirstNonEmpty(prefs.Get(SessionPreferenceKeys.WorkLocationDisplay, string.Empty), "—");

    public static string FirstNonEmpty(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
