namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Canonical first-run and returning-user screen order for the tray journey.
/// Matches the OneXso WorkPulse mockup sequence (activation → setup → workday).
/// </summary>
public static class SetupFlow
{
    public const string Connect = "//connect";
    public const string ConfirmDetails = "//review";
    public const string FaceEnrollment = "//photo";
    public const string LocationThenPermissions = "//location?next=policy";
    public const string Permissions = "//policy";
    public const string Privacy = "//privacy";
    public const string ConfirmDevice = "//device";
    public const string Prepare = "//prepare";
    public const string WelcomeBack = "//prepare?mode=welcome";
    public const string ClockIn = "//clockin";
    public const string Active = "//active";
    public const string End = "//end";

    public static string AfterActivation => ConfirmDetails;
    public static string AfterConfirmDetails => FaceEnrollment;
    public static string AfterFaceEnrollment => LocationThenPermissions;
    public static string AfterPermissions => Privacy;
    public static string AfterPrivacy => ConfirmDevice;
    public static string AfterConfirmDevice => Prepare;
    public static string AfterWorkspaceReady => ClockIn;

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
