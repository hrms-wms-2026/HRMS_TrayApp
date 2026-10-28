namespace ONEVO.Agent.Service.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public sealed class OptionsValidation(IHostEnvironment environment) : IValidateOptions<AgentOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        var errors = new List<string>();

        // Development-only bypasses: if one leaks into a UAT/production config the Service must
        // refuse to start rather than silently skip monitoring gates or IPC path checks (AUD-013).
        if (!environment.IsDevelopment())
        {
            if (options.ForceMonitoringActive)
                errors.Add(DevOnly(nameof(AgentOptions.ForceMonitoringActive)));
            if (options.AllowLocalLifecycleWithoutFullGates)
                errors.Add(DevOnly(nameof(AgentOptions.AllowLocalLifecycleWithoutFullGates)));
            if (options.AllowIpcClientFromAnyPath)
                errors.Add(DevOnly(nameof(AgentOptions.AllowIpcClientFromAnyPath)));
        }

        if (options.HeartbeatIntervalSeconds < 10)
            errors.Add("HeartbeatIntervalSeconds must be >= 10");

        if (options.IngestIntervalSeconds < 30)
            errors.Add("IngestIntervalSeconds must be >= 30");

        if (options.QueueCapacityMb is < 1 or > 1000)
            errors.Add("QueueCapacityMb must be between 1 and 1000");

        if (options.QueueMaxRecords is < 100 or > 1_000_000)
            errors.Add("QueueMaxRecords must be between 100 and 1000000");

        if (options.QueueReservedRecords is < 1 or > 100_000)
            errors.Add("QueueReservedRecords must be between 1 and 100000");

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

    private string DevOnly(string flag) =>
        $"{flag} is a development-only flag and must be false outside Development (environment: {environment.EnvironmentName})";
}
