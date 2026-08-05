using Microsoft.Extensions.Options;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ONEVO Agent Service")
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AgentOptions>()
            .Bind(context.Configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentOptions>, OptionsValidation>();

        services.AddSingleton<AgentStateMachine>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<DeviceIdentityStore>();

        services.AddHostedService<AgentWorker>();
    })
    .Build();

await host.RunAsync();
