namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class PreferencesWorkLocationStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsTypedReference()
    {
        var prefs = new FakePreferencesStore();
        var store = new PreferencesWorkLocationStore(prefs);
        var reference = new WorkLocationReference(
            WorkLocationKind.Office, "OFFICE", "Office",
            6.9271, 79.8612, 12, 300, DateTimeOffset.Parse("2026-08-27T01:00:00Z"));

        store.Save(reference);

        Assert.Equal(reference, store.Load());
    }

    [Fact]
    public void SessionClear_RemovesSavedReference()
    {
        var prefs = new FakePreferencesStore();
        var store = new PreferencesWorkLocationStore(prefs);
        store.Save(new WorkLocationReference(
            WorkLocationKind.Office, "OFFICE", "Office",
            6.9271, 79.8612, 12, 300, DateTimeOffset.UtcNow));

        SessionPreferenceKeys.ClearAll(prefs);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_MalformedData_ReturnsNullWithoutThrowing()
    {
        var prefs = new FakePreferencesStore();
        prefs.Set(SessionPreferenceKeys.WorkLocationReference, "not valid json");
        var store = new PreferencesWorkLocationStore(prefs);

        var exception = Record.Exception(() => store.Load());

        Assert.Null(exception);
        Assert.Null(store.Load());
    }
}
