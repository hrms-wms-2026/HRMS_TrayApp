using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.Services;

/// <summary>
/// AUD-019: the live GPS fix is kept on disk only for the day it was captured and is never
/// returned (or attached to a face photo) on a later day.
/// </summary>
public sealed class LiveLocationPreferenceTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 29, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public void SavedToday_IsReturnedToday()
    {
        var prefs = new FakePreferencesStore();
        LiveLocationPreference.Save(prefs, 6.9271, 79.8612, Today);

        var fix = LiveLocationPreference.TryGetToday(prefs, Today.AddHours(8));

        Assert.Equal((6.9271, 79.8612), fix);
        Assert.Equal("2026-10-29", prefs.Get(SessionPreferenceKeys.LiveLocationCapturedOn, ""));
    }

    [Fact]
    public void SavedYesterday_IsNotReturned_AndIsRemoved()
    {
        var prefs = new FakePreferencesStore();
        LiveLocationPreference.Save(prefs, 6.9271, 79.8612, Today.AddDays(-1));

        Assert.Null(LiveLocationPreference.TryGetToday(prefs, Today));
        AssertNothingStored(prefs);
    }

    [Fact]
    public void UndatedFixFromAnOlderBuild_IsNotReturned_AndIsRemoved()
    {
        var prefs = new FakePreferencesStore();
        prefs.Set(SessionPreferenceKeys.LiveLatitude, "6.9271");
        prefs.Set(SessionPreferenceKeys.LiveLongitude, "79.8612");

        Assert.Null(LiveLocationPreference.TryGetToday(prefs, Today));
        AssertNothingStored(prefs);
    }

    [Fact]
    public void SignOutClearAll_RemovesTheCaptureDateToo()
    {
        var prefs = new FakePreferencesStore();
        LiveLocationPreference.Save(prefs, 6.9271, 79.8612, Today);

        SessionPreferenceKeys.ClearAll(prefs);

        AssertNothingStored(prefs);
    }

    private static void AssertNothingStored(FakePreferencesStore prefs)
    {
        Assert.Equal("", prefs.Get(SessionPreferenceKeys.LiveLatitude, ""));
        Assert.Equal("", prefs.Get(SessionPreferenceKeys.LiveLongitude, ""));
        Assert.Equal("", prefs.Get(SessionPreferenceKeys.LiveLocationCapturedOn, ""));
    }
}
