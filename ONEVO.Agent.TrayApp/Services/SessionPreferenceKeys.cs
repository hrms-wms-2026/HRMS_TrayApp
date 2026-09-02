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
    public const string Department = "onevo.employee_department";
    public const string WorkMode = "onevo.employee_work_mode";
    public const string OfficeName = "onevo.employee_office";
    public const string Organization = "onevo.employee_organization";
    public const string FaceVerified = "onevo.face_verified";
    public const string DeviceName = "onevo.device_name";
    public const string LiveLatitude = "onevo.live_latitude";
    public const string LiveLongitude = "onevo.live_longitude";
    public const string WorkLocationCode = "onevo.work_location_code";
    public const string WorkLocationDisplay = "onevo.work_location_display";
    public const string WorkLocationReference = "onevo.work_location_reference";
    public const string WorkLocationConfirmedOn = "onevo.work_location_confirmed_on";
    public const string SetupCompleted = "onevo.setup_completed";

    public static readonly IReadOnlyList<string> All =
    [
        EmployeeDisplayName,
        EmployeeEmail,
        EmployeeId,
        Department,
        WorkMode,
        OfficeName,
        Organization,
        FaceVerified,
        DeviceName,
        LiveLatitude,
        LiveLongitude,
        WorkLocationCode,
        WorkLocationDisplay,
        WorkLocationReference,
        WorkLocationConfirmedOn,
        SetupCompleted
    ];

    public static void ClearAll(IPreferencesStore preferences)
    {
        foreach (var key in All)
            preferences.Remove(key);
    }
}
