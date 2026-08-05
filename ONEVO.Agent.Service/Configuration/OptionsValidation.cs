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

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
