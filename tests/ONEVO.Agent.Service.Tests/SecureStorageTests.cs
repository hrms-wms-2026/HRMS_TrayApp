namespace ONEVO.Agent.Service.Tests;

using System.Security.AccessControl;
using System.Security.Principal;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

public sealed class SecureStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onevo-storage-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Identity_RoundTrips_AndOverwritesAtomically_WithoutLeftoverTempFiles()
    {
        var store = new DeviceIdentityStore(_dir);
        store.Save(new DeviceIdentity { DeviceId = "d1", AgentId = "a1", TenantId = "t1" });
        store.Save(new DeviceIdentity { DeviceId = "d2", AgentId = "a2", TenantId = "t2" });

        Assert.Equal("d2", store.Load()!.DeviceId);
        Assert.Equal(new[] { "identity.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [WindowsFact]
    public void Credential_RoundTrips()
    {
        var store = new CredentialStore(_dir);
        store.StoreDeviceJwt("synthetic-test-token");

        Assert.Equal("synthetic-test-token", store.ReadDeviceJwt());
        store.ClearDeviceJwt();
        Assert.Null(store.ReadDeviceJwt());
    }

    [WindowsFact]
    public void CredentialFolderAndFile_AreNotAccessibleToStandardUsers()
    {
        // ISS-010 regression: the folder used to inherit BUILTIN\Users read access from %ProgramData%.
        new CredentialStore(_dir).StoreDeviceJwt("synthetic-test-token");

        AssertOnlySystemAndAdministrators(new DirectoryInfo(_dir).GetAccessControl(), protectedAcl: true);
        AssertOnlySystemAndAdministrators(new FileInfo(Path.Combine(_dir, "credential.dat")).GetAccessControl(), protectedAcl: false);
    }

    [WindowsFact]
    public void PreCreatedFolderGrantingUsers_IsReclaimed()
    {
        // A standard user can pre-create a folder under %ProgramData% before installation (squatting).
        var squatted = new DirectorySecurity();
        squatted.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        squatted.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        squatted.SetAccessRuleProtection(true, false);
        Directory.CreateDirectory(_dir).SetAccessControl(squatted);

        new CredentialStore(_dir).StoreDeviceJwt("synthetic-test-token");

        var acl = new DirectoryInfo(_dir).GetAccessControl();
        AssertOnlySystemAndAdministrators(acl, protectedAcl: true);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            acl.GetOwner(typeof(SecurityIdentifier)));
    }

    private static void AssertOnlySystemAndAdministrators(FileSystemSecurity acl, bool protectedAcl)
    {
        if (protectedAcl)
            Assert.True(acl.AreAccessRulesProtected, "inheritance from %ProgramData% must be blocked");

        var allowed = new[]
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Contains((SecurityIdentifier)r.IdentityReference, allowed));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
