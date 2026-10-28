namespace ONEVO.Agent.Service.Security;

using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Limits agent data (credentials, identity, evidence) to SYSTEM, Administrators and the account the
/// Service runs as. Under %ProgramData% the folder otherwise inherits BUILTIN\Users:(RX), and the
/// files are DPAPI LocalMachine-protected, so any local user could read and decrypt them (AUD-003).
/// </summary>
public static class AgentDataAcl
{
    /// <summary>
    /// Replaces the DACL of <paramref name="path"/> with a protected one. For a directory the rules
    /// are inherited by new children, and the files already in it are restricted as well. Best-effort:
    /// a failure (e.g. a folder created by another account) leaves the existing ACL in place.
    /// </summary>
    public static void TryRestrict(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                directory.SetAccessControl((DirectorySecurity)BuildRestricted(
                    directory.GetAccessControl(), InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));

                foreach (var file in directory.EnumerateFiles())
                    RestrictFile(file);
            }
            else if (File.Exists(path))
            {
                RestrictFile(new FileInfo(path));
            }
        }
        catch
        {
            // ACL hardening is best-effort; the agent still works without it.
        }
    }

    private static void RestrictFile(FileInfo file)
    {
        try
        {
            file.SetAccessControl((FileSecurity)BuildRestricted(file.GetAccessControl(), InheritanceFlags.None));
        }
        catch
        {
            // Best-effort per file (e.g. locked by another process).
        }
    }

    private static FileSystemSecurity BuildRestricted(FileSystemSecurity security, InheritanceFlags inheritance)
    {
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            security.RemoveAccessRuleSpecific(rule);

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        AddFullControl(security, localSystem, inheritance);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), inheritance);

        // A development Service runs as the signed-in user, not LocalSystem; keep its own access.
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null && currentUser != localSystem)
            AddFullControl(security, currentUser, inheritance);

        return security;
    }

    private static void AddFullControl(FileSystemSecurity security, SecurityIdentifier sid, InheritanceFlags inheritance) =>
        security.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
}
