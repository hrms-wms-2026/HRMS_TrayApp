using Microsoft.Extensions.Options;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ONEVO Agent Service")
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AgentOptions>()
            .Bind(context.Configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentOptions>, OptionsValidation>();

        services.AddSingleton<AgentStateMachine>();
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<ActivityRecordBuffer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            return new ActivityRecordBuffer(opts.QueueMaxRecords);
        });
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<DeviceIdentityStore>();
        services.AddSingleton<NamedPipeAuthenticator>();
        services.AddSingleton<NamedPipeServer>();

        services.AddHttpClient("OnevoApi", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl))
                client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.HttpTimeoutSeconds, 5, 300));
        });

        services.AddHostedService<AgentWorker>();
        services.AddHostedService<ActivitySyncService>();
        services.AddHostedService<HeartbeatService>();
    })
    .Build();

await host.RunAsync();
