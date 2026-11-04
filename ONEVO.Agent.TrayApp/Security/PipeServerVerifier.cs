namespace ONEVO.Agent.TrayApp.Security;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Checks who created the server end of the agent pipe before the Tray App sends it anything.
/// Any local process can create a pipe with the agent's name while the Service is stopped or
/// restarting; without this check the Tray App would hand that process face photos, evidence and
/// clock-in requests, and trust whatever it answers (fake liveness credentials, update links).
/// A standard user cannot make a pipe whose owner is SYSTEM or Administrators, so the owner is
/// what proves the server is the installed Service (which runs as LocalSystem).
/// </summary>
public static class PipeServerVerifier
{
    /// <summary>
    /// Debug builds also accept a pipe owned by the signed-in user: a development Service started
    /// with <c>dotnet run</c> runs as that user. Release builds accept only SYSTEM/Administrators.
    /// </summary>
    public static bool AllowCurrentUserOwnerByDefault =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Returns null when the server is trusted, otherwise the reason it is not.</summary>
    public static string? Check(PipeStream pipe, bool allowCurrentUserOwner)
    {
        SecurityIdentifier? owner;
        try
        {
            owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex)
        {
            return $"pipe owner could not be read ({ex.GetType().Name})";
        }

        return CheckOwner(owner, WindowsIdentity.GetCurrent().User, allowCurrentUserOwner);
    }

    internal static string? CheckOwner(
        SecurityIdentifier? owner, SecurityIdentifier? currentUser, bool allowCurrentUserOwner)
    {
        if (owner is null)
            return "pipe owner unknown";

        if (owner.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            return null;

        if (allowCurrentUserOwner && currentUser is not null && owner == currentUser)
            return null;

        return $"pipe is owned by {owner.Value}, not the Agent Service";
    }
}
