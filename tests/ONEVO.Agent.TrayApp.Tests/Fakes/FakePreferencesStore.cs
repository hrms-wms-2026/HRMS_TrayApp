namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakePreferencesStore : IPreferencesStore
{
    private readonly Dictionary<string, string> _values = new();

    public string Get(string key, string defaultValue) =>
        _values.TryGetValue(key, out var value) ? value : defaultValue;

    public void Set(string key, string value) => _values[key] = value;

    public void Remove(string key) => _values.Remove(key);
}
