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
    public const string Summary = "//summary";

    public static string AfterActivation => ConfirmDetails;
    public static string AfterConfirmDetails => FaceEnrollment;

    /// <summary>
    /// Work modes without "Let employee choose daily" never need the office/home/other screen -
    /// not daily, and not this one-time capture during first setup either. Their location behavior
    /// is already fixed by the work mode (office-checked, or self-registered from their first real
    /// clock-in/check-in) - see WorkLocationFlow.RouteToStartWork for the equivalent daily-return
    /// skip.
    /// </summary>
    public static string AfterFaceEnrollment(bool allowsDailyLocationChoice) =>
        allowsDailyLocationChoice ? LocationThenPrivacy : Privacy;

    public static string AfterPrivacy => Permissions;
    public static string AfterPermissions => Prepare;
    public static string AfterWorkspaceReady => ClockIn;

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
