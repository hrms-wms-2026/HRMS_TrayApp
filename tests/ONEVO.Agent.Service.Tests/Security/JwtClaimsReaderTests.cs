namespace ONEVO.Agent.Service.Tests.Security;

using System.Text;
using System.Text.Json;
using ONEVO.Agent.Service.Security;
using Xunit;

public class JwtClaimsReaderTests
{
    [Fact]
    public void ReadDeviceClaims_ValidToken_ExtractsSubAndTenantId()
    {
        var jwt = BuildFakeJwt(new { sub = "device-123", tenant_id = "tenant-456" });

        var (deviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(jwt);

        Assert.Equal("device-123", deviceId);
        Assert.Equal("tenant-456", tenantId);
    }

    [Fact]
    public void ReadDeviceClaims_MissingClaims_ReturnsNulls()
    {
        var jwt = BuildFakeJwt(new { token_type = "tray_device" });

        var (deviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(jwt);

        Assert.Null(deviceId);
        Assert.Null(tenantId);
    }

    // AUD-004: queued rows are bound to the employee (tenant + user), not the device registration
    // (sub), which changes when the same employee re-enrolls.
    [Fact]
    public void ReadOwnerKey_TrayToken_CombinesTenantAndUser()
    {
        var jwt = FakeJwt.ForEmployee(tenantId: "tenant-1", userId: "user-1");

        Assert.Equal("tenant-1:user-1", JwtClaimsReader.ReadOwnerKey(jwt));
    }

    [Theory]
    [InlineData("""{"tenant_id":"tenant-1"}""")]
    [InlineData("""{"user_id":"user-1"}""")]
    public void ReadOwnerKey_MissingClaim_ReturnsNull(string payloadJson)
    {
        var jwt = FakeJwt.Build(JsonDocument.Parse(payloadJson).RootElement);

        Assert.Null(JwtClaimsReader.ReadOwnerKey(jwt));
    }

    [Fact]
    public void ReadOwnerKey_NotAJwt_ReturnsNull() =>
        Assert.Null(JwtClaimsReader.ReadOwnerKey("test-jwt"));

    [Fact]
    public void ReadExpiry_ValidToken_ReturnsExpAsUtcInstant()
    {
        var jwt = BuildFakeJwt(new { sub = "device-123", exp = 1_800_000_000L });

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000L), JwtClaimsReader.ReadExpiry(jwt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public void ReadExpiry_MalformedOrMissing_ReturnsNull(string malformed)
    {
        Assert.Null(JwtClaimsReader.ReadExpiry(malformed));
        Assert.Null(JwtClaimsReader.ReadExpiry(BuildFakeJwt(new { sub = "device-123" })));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one-dot-missing")]
    public void ReadDeviceClaims_MalformedToken_ReturnsNulls_NeverThrows(string malformed)
    {
        var (deviceId, tenantId) = JwtClaimsReader.ReadDeviceClaims(malformed);

        Assert.Null(deviceId);
        Assert.Null(tenantId);
    }

    private static string BuildFakeJwt(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.unsigned";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
