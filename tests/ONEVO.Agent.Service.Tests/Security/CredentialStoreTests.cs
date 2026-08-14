namespace ONEVO.Agent.Service.Tests.Security;

using ONEVO.Agent.Service.Security;
using Xunit;

/// <summary>
/// CredentialStore reads/writes DPAPI-protected files under %ProgramData%\ONEVO\Agent\,
/// the same location the real Service uses — each test clears what it wrote so it
/// doesn't leak state into other tests or a real dev Service running on this machine.
/// </summary>
[Collection(CredentialStoreFileCollection.Name)]
public class CredentialStoreTests
{
    [Fact]
    public void DeviceJwt_RoundTrips()
    {
        var store = new CredentialStore();
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
        var store = new CredentialStore();
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
        var store = new CredentialStore();
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
    public void ReadRefreshToken_WhenNeverStored_ReturnsNull()
    {
        var store = new CredentialStore();
        store.ClearRefreshToken(); // ensure clean slate regardless of test order
        Assert.Null(store.ReadRefreshToken());
    }
}
