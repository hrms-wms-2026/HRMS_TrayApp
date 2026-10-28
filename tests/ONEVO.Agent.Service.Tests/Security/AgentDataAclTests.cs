namespace ONEVO.Agent.Service.Tests.Security;

using System.Security.AccessControl;
using System.Security.Principal;
using ONEVO.Agent.Service.Security;
using Xunit;

/// <summary>
/// AUD-003: %ProgramData%\ONEVO\Agent inherited BUILTIN\Users:(RX) from ProgramData, and the
/// credential files there are DPAPI LocalMachine-protected — any local user could read and
/// decrypt the device and refresh tokens. The folder and its files must only be reachable by
/// SYSTEM, Administrators and the account the Service runs as.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class AgentDataAclTests
{
    private static readonly SecurityIdentifier[] BroadPrincipals =
    [
        new(WellKnownSidType.BuiltinUsersSid, null),
        new(WellKnownSidType.AuthenticatedUserSid, null),
        new(WellKnownSidType.WorldSid, null),
        new(WellKnownSidType.InteractiveSid, null)
    ];

    private static string NewDirectoryOpenToUsers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"onevo-acl-{Guid.NewGuid():N}");
        var info = Directory.CreateDirectory(dir);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
        return dir;
    }

    private static bool GrantsBroadAccess(FileSystemSecurity security) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow
                         && BroadPrincipals.Contains((SecurityIdentifier)rule.IdentityReference));

    [Fact]
    public void TryRestrict_RemovesBroadAccessFromDirectoryAndExistingFiles()
    {
        var dir = NewDirectoryOpenToUsers();
        var existing = Path.Combine(dir, "credential.dat");
        File.WriteAllText(existing, "secret");
        Assert.True(GrantsBroadAccess(new FileInfo(existing).GetAccessControl())); // precondition

        AgentDataAcl.TryRestrict(dir);

        var dirSecurity = new DirectoryInfo(dir).GetAccessControl();
        Assert.True(dirSecurity.AreAccessRulesProtected);
        Assert.False(GrantsBroadAccess(dirSecurity));
        Assert.False(GrantsBroadAccess(new FileInfo(existing).GetAccessControl()));
        Assert.Equal("secret", File.ReadAllText(existing)); // the Service account keeps access
    }

    [Fact]
    public void TryRestrict_FilesCreatedAfterwardsInheritTheRestriction()
    {
        var dir = NewDirectoryOpenToUsers();

        AgentDataAcl.TryRestrict(dir);
        var created = Path.Combine(dir, "refresh.dat");
        File.WriteAllText(created, "secret");

        Assert.False(GrantsBroadAccess(new FileInfo(created).GetAccessControl()));
    }

    [Fact]
    public void CredentialStore_RestrictsItsDirectoryAndTokenFiles()
    {
        var dir = NewDirectoryOpenToUsers();

        var store = new CredentialStore(dir);
        store.StoreDeviceJwt("jwt");
        store.StoreRefreshToken("refresh");

        Assert.False(GrantsBroadAccess(new DirectoryInfo(dir).GetAccessControl()));
        Assert.False(GrantsBroadAccess(new FileInfo(Path.Combine(dir, "credential.dat")).GetAccessControl()));
        Assert.False(GrantsBroadAccess(new FileInfo(Path.Combine(dir, "refresh.dat")).GetAccessControl()));
        Assert.Equal("jwt", store.ReadDeviceJwt());
    }
}
