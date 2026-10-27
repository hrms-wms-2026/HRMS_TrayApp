namespace ONEVO.Agent.Service.Tests.Security;

using ONEVO.Agent.Service.Security;
using Xunit;

/// <summary>
/// CredentialStore reads/writes DPAPI-protected files. These tests use the shared per-run temp
/// directory from <see cref="TestStores"/>, never the real %ProgramData%\ONEVO\Agent\ files, and
/// each test clears what it wrote so it doesn't leak state into other tests.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class CredentialStoreTests
{
    [Fact]
    public void DeviceJwt_RoundTrips()
    {
        var store = TestStores.NewCredentialStore();
        try
        {
            store.StoreDeviceJwt("test-jwt-value");
            Assert.Equal("test-jwt-value", store.ReadDeviceJwt());
        }
        finally
        {
            store.ClearDeviceJwt();
        }
    }

    [Fact]
    public void RefreshToken_RoundTrips_IndependentlyOfDeviceJwt()
    {
        var store = TestStores.NewCredentialStore();
        try
        {
            store.StoreDeviceJwt("jwt-value");
            store.StoreRefreshToken("refresh-value");

            Assert.Equal("jwt-value", store.ReadDeviceJwt());
            Assert.Equal("refresh-value", store.ReadRefreshToken());

            store.ClearRefreshToken();

            Assert.Null(store.ReadRefreshToken());
            Assert.Equal("jwt-value", store.ReadDeviceJwt()); // clearing one must not touch the other
        }
        finally
        {
            store.ClearDeviceJwt();
            store.ClearRefreshToken();
        }
    }

    [Fact]
    public void RefreshToken_Rotation_OverwritesPreviousValue()
    {
        var store = TestStores.NewCredentialStore();
        try
        {
            store.StoreRefreshToken("first");
            store.StoreRefreshToken("second");

            Assert.Equal("second", store.ReadRefreshToken());
        }
        finally
        {
            store.ClearRefreshToken();
        }
    }

    [Fact]
    public void DirectoryOverride_KeepsTokensInsideThatDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"onevo-cred-{Guid.NewGuid():N}");
        var store = new CredentialStore(dir);

        store.StoreDeviceJwt("isolated-jwt");
        store.StoreRefreshToken("isolated-refresh");

        Assert.True(File.Exists(Path.Combine(dir, "credential.dat")));
        Assert.True(File.Exists(Path.Combine(dir, "refresh.dat")));
        Assert.Equal("isolated-jwt", new CredentialStore(dir).ReadDeviceJwt());

        store.ClearDeviceJwt();
        store.ClearRefreshToken();
        Assert.False(File.Exists(Path.Combine(dir, "credential.dat")));
        Assert.False(File.Exists(Path.Combine(dir, "refresh.dat")));
    }

    [Fact]
    public void ReadRefreshToken_WhenNeverStored_ReturnsNull()
    {
        var store = TestStores.NewCredentialStore();
        store.ClearRefreshToken(); // ensure clean slate regardless of test order
        Assert.Null(store.ReadRefreshToken());
    }
}
