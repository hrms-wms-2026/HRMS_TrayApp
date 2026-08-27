namespace ONEVO.Agent.Service.Configuration;

using Microsoft.Extensions.Options;

public sealed class OptionsValidation : IValidateOptions<AgentOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        var errors = new List<string>();

        if (options.HeartbeatIntervalSeconds < 10)
            errors.Add("HeartbeatIntervalSeconds must be >= 10");

        if (options.IngestIntervalSeconds < 30)
            errors.Add("IngestIntervalSeconds must be >= 30");

        if (options.QueueCapacityMb is < 1 or > 1000)
            errors.Add("QueueCapacityMb must be between 1 and 1000");

        if (options.HttpTimeoutSeconds is < 5 or > 300)
            errors.Add("HttpTimeoutSeconds must be between 5 and 300");

        if (string.IsNullOrWhiteSpace(options.PortalRootDomain)
            || options.PortalRootDomain.Contains('/')
            || options.PortalRootDomain.Contains(' '))
            errors.Add("PortalRootDomain must be a host name without a path");

        if (options.PortalProductionPort is < 1 or > 65535)
            errors.Add("PortalProductionPort must be between 1 and 65535");

        if (options.PortalDevelopmentPorts.Any(port => port is < 1 or > 65535))
            errors.Add("PortalDevelopmentPorts must contain valid TCP ports");

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            errors.Add("ApiBaseUrl must be set — enrollment/login cannot reach the backend without it");
        else if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            errors.Add("ApiBaseUrl must be an absolute https:// URL");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
