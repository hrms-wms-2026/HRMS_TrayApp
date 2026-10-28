namespace ONEVO.Agent.Service.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Builds the enrolled tenant's portal address (https://{slug}.{PortalRootDomain}[:port]) that the
/// Service pushes to the Tray, so "Open dashboard", notification clicks and policy links go to the
/// employee's own workspace instead of one hard-coded demo tenant (AUD-030). Development uses the
/// first PortalDevelopmentPorts entry (the local Angular server), every other environment
/// PortalProductionPort. The slug comes from the backend at enrollment and is validated as a
/// single DNS label before it goes into a host name.
/// </summary>
public sealed class PortalLinks(IOptions<AgentOptions> options, IHostEnvironment environment)
{
    private readonly AgentOptions _options = options.Value;

    public string? BaseUrlFor(string? tenantSlug)
    {
        var slug = tenantSlug?.Trim().ToLowerInvariant();
        if (!IsDnsLabel(slug))
            return null;

        var port = environment.IsDevelopment()
            ? _options.PortalDevelopmentPorts.FirstOrDefault(443)
            : _options.PortalProductionPort;
        var builder = new UriBuilder(Uri.UriSchemeHttps, $"{slug}.{_options.PortalRootDomain}", port == 443 ? -1 : port);
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsDnsLabel(string? label)
    {
        if (string.IsNullOrEmpty(label) || label.Length > 63 || label[0] == '-' || label[^1] == '-')
            return false;
        foreach (var c in label)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                return false;
        }
        return true;
    }
}
