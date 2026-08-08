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
