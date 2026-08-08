namespace ONEVO.Agent.Service.Security;

using System.Text;
using System.Text.Json;

/// <summary>
/// Reads the unverified payload of the device JWT to recover local bookkeeping
/// values (device id, tenant id). This is not a trust boundary — the Service
/// already received this token directly from the backend over TLS; signature
/// verification happens on every subsequent call the backend receives, not here.
/// </summary>
public static class JwtClaimsReader
{
    public static (string? DeviceId, string? TenantId) ReadDeviceClaims(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return (null, null);

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            var root = doc.RootElement;
            var deviceId = root.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
            var tenantId = root.TryGetProperty("tenant_id", out var tenant) ? tenant.GetString() : null;
            return (deviceId, tenantId);
        }
        catch
        {
            return (null, null);
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
