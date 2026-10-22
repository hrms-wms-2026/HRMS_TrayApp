namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Canonical first-run and returning-user screen order for the tray journey.
/// Matches the OneXso WorkPulse mockup sequence (activation → setup → workday).
/// </summary>
public static class SetupFlow
{
    public const string Connect = "//connect";
    public const string LegalConsent = "//legal-consent";
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
    /// <remarks>
    /// When the employee's privacy/permissions acceptance is already on the backend (see
    /// <see cref="ConsentCovers"/>), the Privacy and Permissions screens are skipped.
    /// </remarks>
    public static string AfterFaceEnrollment(bool allowsDailyLocationChoice, bool consentOnFile = false) =>
        consentOnFile
            ? allowsDailyLocationChoice ? WorkLocationFlow.LocationThenPrepare : Prepare
            : allowsDailyLocationChoice ? LocationThenPrivacy : Privacy;

    /// <summary>
    /// The employee's latest acceptance still covers what the current policy collects: screen
    /// monitoring, plus app tracking and the camera whenever the policy turns those on. A policy
    /// that starts collecting something they did not allow brings the screens back.
    /// </summary>
    public static bool ConsentCovers(PermissionConsentResultPayload? status, AgentPolicy? policy)
    {
        if (status is not { Success: true, Accepted: true, Consent: { ScreenMonitoring: true } consent })
            return false;

        if (policy is null)
            return true;

        if (policy.AppUsageEnabled && !consent.AppTracking)
            return false;

        return !(policy.CameraVerificationEnabled || policy.PhotoRequiredEnabled) || consent.CameraAccess;
    }

    public static string AfterPrivacy => Permissions;
    public static string AfterPermissions => Prepare;
    public static string AfterWorkspaceReady => ClockIn;

    public static string DisplayOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
