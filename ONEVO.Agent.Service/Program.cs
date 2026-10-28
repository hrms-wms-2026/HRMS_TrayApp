using Microsoft.Extensions.Options;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Diagnostics;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;

using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Sync;

if (QuarantineCommands.IsCommand(args))
    return QuarantineCommands.Run(args, Console.Out);

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ONEVO Agent Service")
    // Level filters come from the "Logging" section, like the other providers.
    .ConfigureLogging(logging => logging.AddProvider(ServiceLogging.CreateFileProvider()))
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AgentOptions>()
            .Bind(context.Configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentOptions>, OptionsValidation>();

        services.AddSingleton<AgentStateMachine>();
        services.AddSingleton<LifecycleGate>();
        services.AddSingleton<PresenceSession>();
        services.AddSingleton<ISystemPowerEvents, SystemPowerEvents>();
        services.AddHostedService<PowerModeIdleListener>();
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<IEvidenceProtector, DpapiEvidenceProtector>();
        services.AddSingleton<EvidenceSpoolStore>();
        services.AddSingleton<EvidenceTransferAssembler>();
        services.AddSingleton<InactivityEvidenceHandler>();
        services.AddSingleton<PeriodicScreenshotHandler>();
        services.AddSingleton<ActivityRecordBuffer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ActivityRecordBuffer");
            var path = ActivityRecordBuffer.GetDefaultDatabasePath();
            var credentials = sp.GetRequiredService<CredentialStore>();
            logger.LogInformation(
                "SQLite activity store: {Path} (max pending {Max} + {Reserved} reserved, max {Mb} MB)",
                path, opts.QueueMaxRecords, opts.QueueReservedRecords, opts.QueueCapacityMb);
            return new ActivityRecordBuffer(
                path, opts.QueueMaxRecords, opts.QueueCapacityMb * 1024L * 1024, opts.QueueReservedRecords, logger,
                ownerKeyProvider: () => credentials.ReadDeviceJwt() is { } jwt ? JwtClaimsReader.ReadOwnerKey(jwt) : null);
        });
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<DeviceIdentityStore>();
        services.AddSingleton<PendingLegalChallengeStore>();
        services.AddSingleton<NamedPipeAuthenticator>();
        services.AddSingleton<IClientProcessInspector, WindowsClientProcessInspector>();
        services.AddSingleton<IIpcClientVerifier, IpcClientVerifier>();
        services.AddSingleton<NamedPipeServer>();
        services.AddSingleton<IIpcBroadcaster>(sp => sp.GetRequiredService<NamedPipeServer>());

        // packages-guide §9 — Polly v8 via Microsoft.Extensions.Http.Resilience
        services.AddHttpClient("OnevoApi", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl))
                client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
            // Overall timeout handled by resilience pipeline attempt timeout + retries
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddHttpMessageHandler(() => new ONEVO.Agent.Service.Api.CorrelationIdHandler())
        .AddResilienceHandler("onevo-api-pipeline", (builder, context) =>
        {
            var httpTimeout = context.ServiceProvider
                .GetRequiredService<IOptions<AgentOptions>>().Value.HttpTimeoutSeconds;
            OnevoApiResilience.Configure(builder, OnevoApiResilience.AttemptTimeout(httpTimeout));
        });

        services.AddSingleton<ONEVO.Agent.Service.Enrollment.PortalOriginValidator>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            return new ONEVO.Agent.Service.Enrollment.PortalOriginValidator(
                options.PortalRootDomain,
                options.PortalProductionPort,
                options.PortalDevelopmentPorts,
                options.PortalDevelopmentOrigins,
                context.HostingEnvironment.IsDevelopment());
        });
        services.AddSingleton<OnevoApiClient>();
        // Single-flight token rotation shared by HeartbeatService and TokenRefreshService.
        services.AddSingleton<DeviceTokenRefresher>();
        services.AddSingleton<EnrollmentCoordinator>();

        services.AddSingleton<ONEVO.Agent.Service.Biometrics.FaceSetupPhotoStaging>();
        services.AddSingleton<AgentWorker>();
        services.AddSingleton<IPresenceReconciler>(sp => sp.GetRequiredService<AgentWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<AgentWorker>());
        services.AddSingleton<ActivitySyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<ActivitySyncService>());
        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<TokenRefreshService>();
        // Registered right after TokenRefreshService per the Task 3 plan ("after token
        // refresh, before activity sync"). ActivitySyncService is left in its existing slot
        // above rather than reordered — DI registration order does not sequence independent
        // BackgroundServices (each runs its own ExecuteAsync as soon as the host starts), and
        // reordering it was not requested. PolicySyncService's own "before activity sync" is
        // achieved instead by its immediate-fetch-on-JWT behavior (see PolicySyncService.cs).
        services.AddSingleton<PolicySyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<PolicySyncService>());
        services.AddSingleton<PushChannelState>();
        // Singletons (not bare hosted services) so AgentCommandListener can trigger a poll on push.
        services.AddSingleton<NotificationPollingService>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationPollingService>());
        services.AddSingleton<AttendanceStatusSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<AttendanceStatusSyncService>());
        // packages-guide §1 — SignalR remote commands (waits for JWT)
        services.AddHostedService<AgentCommandListener>();
    })
    .Build();

await host.RunAsync();

return 0;
