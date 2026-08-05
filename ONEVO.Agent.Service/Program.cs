using ONEVO.Agent.Service;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ONEVO Agent Service")
    .ConfigureServices((_, services) =>
    {
        services.AddHostedService<AgentWorker>();
    })
    .Build();

await host.RunAsync();
