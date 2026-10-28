namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Single source of truth for the browser links the TrayApp opens (Employee Portal, Dashboard).
/// The Service pushes the enrolled tenant's portal origin in every status message
/// (<see cref="StatusResponsePayload.PortalBaseUrl"/>, AUD-030); <see cref="UseTenantPortal"/>
/// applies it. Until the first status arrives, before enrollment, or with an older Service build,
/// the links fall back to the local dev server of the "acme" demo tenant used across local e2e
/// testing (see docs/postman/ONEVO-Local.postman_environment.json).
/// Root ('') and '/dashboard' are NOT interchangeable in app.routes.ts: root is the public
/// marketing landing page (redirectIfAuthenticatedGuard), while '/dashboard' sits behind authGuard
/// in MainLayoutComponent and is what actually sends an unauthenticated visitor to /auth/login or
/// shows the real dashboard.
/// Dev root domain is onexso.com, not localhost (2026-09-04 switch: the local dev TLS cert's SAN
/// list no longer covers *.localhost, so a stale *.localhost link fails TLS validation).
/// </summary>
public static class WorkspaceLinks
{
    public const string FallbackPortalUrl = "https://acme.onexso.com:4200";

    private static volatile string _portalUrl = FallbackPortalUrl;

    public static string PortalUrl => _portalUrl;

    public static string DashboardUrl => _portalUrl + "/dashboard";

    /// <summary>
    /// Points the links at <paramref name="portalBaseUrl"/> when it is an https origin (scheme and
    /// host, nothing else), so a malformed value can never become a ShellExecute target. Anything
    /// else, including null, keeps the current links.
    /// </summary>
    public static bool UseTenantPortal(string? portalBaseUrl)
    {
        if (!Uri.TryCreate(portalBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.PathAndQuery != "/"
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        _portalUrl = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    /// <summary>Tests only: back to the fallback links.</summary>
    internal static void Reset() => _portalUrl = FallbackPortalUrl;
}
