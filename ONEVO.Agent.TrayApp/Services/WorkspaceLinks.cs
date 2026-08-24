namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Single source of truth for the browser links the TrayApp opens (Employee Portal, Dashboard).
/// The production workspace domain does not exist yet, so these currently point at the local
/// Angular dev server for the "acme" demo tenant used across local e2e testing (see
/// docs/postman/ONEVO-Local.postman_environment.json). Root ('') and '/dashboard' are NOT
/// interchangeable in app.routes.ts: root is the public marketing landing page
/// (redirectIfAuthenticatedGuard), while '/dashboard' sits behind authGuard in MainLayoutComponent
/// and is what actually sends an unauthenticated visitor to /auth/login or shows the real
/// dashboard. Update both constants together when a real workspace domain is available.
/// </summary>
public static class WorkspaceLinks
{
    public const string PortalUrl = "https://acme.localhost:4200";
    public const string DashboardUrl = "https://acme.localhost:4200/dashboard";
}
