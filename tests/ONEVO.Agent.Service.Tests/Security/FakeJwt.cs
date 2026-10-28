namespace ONEVO.Agent.Service.Tests.Security;

using System.Text.Json;

/// <summary>Unsigned JWT-shaped strings for tests that only read claims locally.</summary>
internal static class FakeJwt
{
    public static string Build(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.unsigned";
    }

    /// <summary>A tray device token for <paramref name="userId"/> in <paramref name="tenantId"/>.</summary>
    public static string ForEmployee(string tenantId, string userId) =>
        Build(new { sub = Guid.NewGuid().ToString(), user_id = userId, tenant_id = tenantId, token_type = "tray_device" });

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
