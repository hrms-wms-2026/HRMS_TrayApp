namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Thin seam over platform Preferences storage so ViewModels are testable outside a running MAUI app.</summary>
public interface IPreferencesStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
    void Remove(string key);
}
