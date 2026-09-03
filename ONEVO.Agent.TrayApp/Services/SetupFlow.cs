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
    public const string LocationThenPrivacy = "//location?next=privacy";
    public const string Permissions = "//policy";
    public const string Privacy = "//privacy";
    public const string Prepare = "//prepare";
    public const string WelcomeBack = "//prepare?mode=welcome";
    public const string ClockIn = "//clockin";
    public const string Active = "//active";
    public const string End = "//end";

    public static string AfterActivation => ConfirmDetails;
    public static string AfterConfirmDetails => FaceEnrollment;
    public static string AfterFaceEnrollment => LocationThenPrivacy;
    public static string AfterPrivacy => Permissions;
    public static string AfterPermissions => Prepare;
    public static string AfterWorkspaceReady => ClockIn;

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
