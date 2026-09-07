namespace ONEVO.Agent.Service.Tests.Security;

using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;
using Xunit;

public class DeviceIdentityStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsIdentity_UsingInjectedDirectory_NotTheRealProgramDataPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new DeviceIdentityStore(tempDir);
            var identity = new DeviceIdentity
            {
                DeviceId = "device-1",
                AgentId = "agent-1",
                TenantId = "tenant-1",
                DeviceFingerprint = "fingerprint-1"
            };

            store.Save(identity);

            Assert.True(File.Exists(Path.Combine(tempDir, "identity.json")));
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal(identity.DeviceId, loaded!.DeviceId);
            Assert.Equal(identity.TenantId, loaded.TenantId);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Clear_RemovesTheIdentityFileFromTheInjectedDirectoryOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new DeviceIdentityStore(tempDir);
            store.Save(new DeviceIdentity
            {
                DeviceId = "device-1",
                AgentId = "agent-1",
                TenantId = "tenant-1",
                DeviceFingerprint = "fingerprint-1"
            });

            store.Clear();

            Assert.Null(store.Load());
            Assert.False(File.Exists(Path.Combine(tempDir, "identity.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TwoStoresWithDifferentInjectedDirectories_DoNotSeeEachOthersIdentity()
    {
        var tempDirA = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempDirB = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var storeA = new DeviceIdentityStore(tempDirA);
            var storeB = new DeviceIdentityStore(tempDirB);
            storeA.Save(new DeviceIdentity
            {
                DeviceId = "device-a",
                AgentId = "agent-a",
                TenantId = "tenant-a",
                DeviceFingerprint = "fingerprint-a"
            });

            Assert.Null(storeB.Load());
        }
        finally
        {
            if (Directory.Exists(tempDirA)) Directory.Delete(tempDirA, recursive: true);
            if (Directory.Exists(tempDirB)) Directory.Delete(tempDirB, recursive: true);
        }
    }
}
