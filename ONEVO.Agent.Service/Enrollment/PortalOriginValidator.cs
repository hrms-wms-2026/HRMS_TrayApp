using System.Net;
using System.Text.RegularExpressions;

namespace ONEVO.Agent.Service.Enrollment;

public sealed class PortalOriginValidator
{
    private static readonly Regex HostLabel = new("^[a-z0-9.-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly string _rootDomain;
    private readonly int _productionPort;
    private readonly IReadOnlySet<int> _developmentPorts;
    private readonly IReadOnlySet<string> _developmentOrigins;
    private readonly bool _development;

    public PortalOriginValidator(
        string rootDomain,
        int productionPort,
        IEnumerable<int>? developmentPorts = null,
        IEnumerable<string>? developmentOrigins = null,
        bool development = false)
    {
        _rootDomain = rootDomain.Trim().TrimEnd('.').ToLowerInvariant();
        _productionPort = productionPort;
        _developmentPorts = (developmentPorts ?? []).ToHashSet();
        _developmentOrigins = (developmentOrigins ?? [])
            .Select(NormalizeConfiguredOrigin)
            .Where(x => x is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _development = development;
    }

    public bool TryNormalize(string? value, out string? normalizedOrigin)
    {
        normalizedOrigin = null;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (uri.AbsolutePath is not ("" or "/"))
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4)
            || !HostLabel.IsMatch(uri.Host)
            || !IsAllowedHost(uri.Host)
            || !IsAllowedPort(uri))
        {
            return false;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (_development && _developmentOrigins.Count > 0 && !_developmentOrigins.Contains(origin))
            return false;

        normalizedOrigin = origin;
        return true;
    }

    private bool IsAllowedHost(string host)
    {
        var normalized = host.TrimEnd('.').ToLowerInvariant();
        return normalized == _rootDomain || normalized.EndsWith("." + _rootDomain, StringComparison.Ordinal);
    }

    private bool IsAllowedPort(Uri uri)
    {
        var port = uri.IsDefaultPort ? 443 : uri.Port;
        return port == _productionPort || (_development && _developmentPorts.Contains(port));
    }

    private static string? NormalizeConfiguredOrigin(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath is "" or "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
            : null;
    }
}
