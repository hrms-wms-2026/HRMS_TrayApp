using System.Text.Json;
using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Serializes the confirmed <see cref="WorkLocationReference"/> to JSON and stores it under
/// <see cref="SessionPreferenceKeys.WorkLocationReference"/> via the injected <see cref="IPreferencesStore"/>
/// seam. Missing or malformed data never crashes the caller — <see cref="Load"/> returns null instead.
/// </summary>
public sealed class PreferencesWorkLocationStore : IWorkLocationStore
{
    private readonly IPreferencesStore _preferences;

    public PreferencesWorkLocationStore(IPreferencesStore preferences)
    {
        _preferences = preferences;
    }

    public void Save(WorkLocationReference reference)
    {
        var json = JsonSerializer.Serialize(reference);
        _preferences.Set(SessionPreferenceKeys.WorkLocationReference, json);
    }

    public WorkLocationReference? Load()
    {
        var json = _preferences.Get(SessionPreferenceKeys.WorkLocationReference, string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<WorkLocationReference>(json);
        }
        catch
        {
            // Malformed/legacy data must never crash the caller — treat it as absent.
            return null;
        }
    }
}
