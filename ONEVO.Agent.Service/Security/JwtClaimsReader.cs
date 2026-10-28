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

    /// <summary>
    /// "tenant_id:user_id" — the employee a tray token belongs to, or null when either claim is
    /// missing. Unlike <c>sub</c> (the device registration) it stays the same when the same
    /// employee re-enrolls, so it can bind queued rows to their owner (AUD-004).
    /// </summary>
    public static string? ReadOwnerKey(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            var root = doc.RootElement;
            var tenantId = root.TryGetProperty("tenant_id", out var tenant) ? tenant.GetString() : null;
            var userId = root.TryGetProperty("user_id", out var user) ? user.GetString() : null;
            return string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId)
                ? null
                : $"{tenantId}:{userId}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The token's <c>exp</c> claim as an absolute UTC instant, or null when it cannot be read.
    /// Callers compare it against wall-clock time, which stays correct across sleep/wake.
    /// </summary>
    public static DateTimeOffset? ReadExpiry(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
