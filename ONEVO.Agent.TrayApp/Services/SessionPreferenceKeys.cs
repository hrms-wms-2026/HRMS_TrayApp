namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Preferences that belong to the currently connected employee/setup session.
/// These values must never leak into the next activation after sign-out.
/// </summary>
public static class SessionPreferenceKeys
{
    public const string EmployeeDisplayName = "onevo.employee_display_name";
    public const string EmployeeEmail = "onevo.employee_email";
    public const string EmployeeId = "onevo.employee_id";
    public const string FaceVerified = "onevo.face_verified";
    public const string LiveLatitude = "onevo.live_latitude";
    public const string LiveLongitude = "onevo.live_longitude";
    public const string WorkLocationCode = "onevo.work_location_code";
    public const string WorkLocationDisplay = "onevo.work_location_display";
    public const string WorkLocationReference = "onevo.work_location_reference";

    public static readonly IReadOnlyList<string> All =
    [
        EmployeeDisplayName,
        EmployeeEmail,
        EmployeeId,
        FaceVerified,
        LiveLatitude,
        LiveLongitude,
        WorkLocationCode,
        WorkLocationDisplay,
        WorkLocationReference
    ];

    public static void ClearAll(IPreferencesStore preferences)
    {
        foreach (var key in All)
            preferences.Remove(key);
    }
}
