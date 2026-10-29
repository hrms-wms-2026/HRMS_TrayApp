namespace ONEVO.Agent.TrayApp.Services;

using System.Globalization;

/// <summary>
/// The GPS fix from the day's work-location confirmation, attached to that day's face photos.
/// Kept on disk only for the day it was captured: a fix from an earlier day (or an undated one from
/// an older build) is removed and never sent as a photo's location (AUD-019).
/// </summary>
public static class LiveLocationPreference
{
    public static void Save(IPreferencesStore prefs, double latitude, double longitude, DateTimeOffset? now = null)
    {
        prefs.Set(SessionPreferenceKeys.LiveLatitude, latitude.ToString("G17", CultureInfo.InvariantCulture));
        prefs.Set(SessionPreferenceKeys.LiveLongitude, longitude.ToString("G17", CultureInfo.InvariantCulture));
        prefs.Set(SessionPreferenceKeys.LiveLocationCapturedOn, WorkLocationFlow.TodayKey(now));
    }

    /// <summary>Today's fix, or null. A fix from any other day is removed.</summary>
    public static (double Latitude, double Longitude)? TryGetToday(IPreferencesStore prefs, DateTimeOffset? now = null)
    {
        var capturedOn = prefs.Get(SessionPreferenceKeys.LiveLocationCapturedOn, string.Empty);
        if (!string.Equals(capturedOn, WorkLocationFlow.TodayKey(now), StringComparison.Ordinal))
        {
            Clear(prefs);
            return null;
        }

        var lat = prefs.Get(SessionPreferenceKeys.LiveLatitude, string.Empty);
        var lon = prefs.Get(SessionPreferenceKeys.LiveLongitude, string.Empty);
        return double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
            && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)
            ? (la, lo)
            : null;
    }

    public static void Clear(IPreferencesStore prefs)
    {
        prefs.Remove(SessionPreferenceKeys.LiveLatitude);
        prefs.Remove(SessionPreferenceKeys.LiveLongitude);
        prefs.Remove(SessionPreferenceKeys.LiveLocationCapturedOn);
    }
}
