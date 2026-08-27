namespace ONEVO.Agent.TrayApp.Services;

public sealed record ActivationProtocolRoute(string PortalOrigin);

public sealed class ActivationProtocolRouter
{
    private readonly Func<string, string?> _normalizeOrigin;

    public ActivationProtocolRouter(Func<string, string?> normalizeOrigin)
    {
        _normalizeOrigin = normalizeOrigin;
    }

    public ActivationProtocolRoute? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "onexso-workspace", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "open", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
        {
            return null;
        }

        var queryParts = uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped)
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (queryParts.Length != 1
            || !queryParts[0].StartsWith("portal_origin=", StringComparison.OrdinalIgnoreCase))
            return null;

        var encodedOrigin = queryParts[0]["portal_origin=".Length..];
        string origin;
        try
        {
            origin = Uri.UnescapeDataString(encodedOrigin);
        }
        catch
        {
            return null;
        }

        var normalized = _normalizeOrigin(origin);
        return normalized is null ? null : new ActivationProtocolRoute(normalized);
    }
}
