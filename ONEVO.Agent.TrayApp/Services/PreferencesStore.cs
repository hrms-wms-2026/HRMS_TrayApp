namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Wraps Microsoft.Maui.Storage.Preferences. Swallows failures the same way every call site did
/// before this seam existed — Preferences throws when there's no running MAUI platform context
/// (e.g. a plain unit test host), and that's not a real error for display-only cached data.
/// </summary>
public sealed class PreferencesStore : IPreferencesStore
{
    public string Get(string key, string defaultValue)
    {
        try { return Preferences.Get(key, defaultValue); }
        catch { return defaultValue; }
    }

    public void Set(string key, string value)
    {
        try { Preferences.Set(key, value); }
        catch { /* no MAUI platform context (e.g. unit tests) */ }
    }
}
