namespace ONEVO.Agent.Service.Security;

using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Agent data folder restricted to SYSTEM and Administrators (ISS-010).
/// %ProgramData% sub-folders otherwise inherit BUILTIN\Users read access, and Users may
/// pre-create folders there, so the ACL and owner are (re)applied on every write.
/// </summary>
internal static class SecureAgentStorage
{
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONEVO", "Agent");

    public static void EnsureDirectory(string directory)
    {
        var info = Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            return;

        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        info.SetAccessControl(security);
    }

    /// <summary>Writes via a uniquely named temp file and an atomic replace (ISS-024).</summary>
    public static void WriteAllBytesAtomic(string path, byte[] contents)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
