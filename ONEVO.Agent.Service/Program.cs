using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using ONEVO.Agent.Service;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.IPC;
using ONEVO.Agent.Service.Lifecycle;
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
        services.AddSingleton<LifecycleGate>();
        services.AddSingleton<PresenceSession>();
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<IEvidenceProtector, DpapiEvidenceProtector>();
        services.AddSingleton<EvidenceSpoolStore>();
        services.AddSingleton<EvidenceTransferAssembler>();
        services.AddSingleton<InactivityEvidenceHandler>();
        services.AddSingleton<ActivityRecordBuffer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ActivityRecordBuffer");
            var path = ActivityRecordBuffer.GetDefaultDatabasePath();
            logger.LogInformation("SQLite activity store: {Path} (max pending {Max})", path, opts.QueueMaxRecords);
            return new ActivityRecordBuffer(path, opts.QueueMaxRecords);
        });
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<DeviceIdentityStore>();
        services.AddSingleton<NamedPipeAuthenticator>();
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
        .AddResilienceHandler("onevo-api-pipeline", (builder, context) =>
        {
            var httpTimeout = context.ServiceProvider
                .GetRequiredService<IOptions<AgentOptions>>().Value.HttpTimeoutSeconds;
            var attemptTimeout = TimeSpan.FromSeconds(Math.Clamp(httpTimeout, 5, 120));

            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException or TaskCanceledException)
                        return PredicateResult.True();
                    var code = args.Outcome.Result?.StatusCode;
                    if (code is HttpStatusCode.TooManyRequests)
                        return PredicateResult.True();
                    if (code is >= HttpStatusCode.InternalServerError)
                        return PredicateResult.True();
                    return PredicateResult.False();
                },
                // Honor Retry-After on 429 when present; else exponential default.
                DelayGenerator = args =>
                {
                    if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests
                        && args.Outcome.Result.Headers.RetryAfter?.Delta is { } ra
                        && ra > TimeSpan.Zero)
                        return new ValueTask<TimeSpan?>(ra);
                    return new ValueTask<TimeSpan?>((TimeSpan?)null);
                }
            });

            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromMinutes(2),
                MinimumThroughput = 5,
                FailureRatio = 0.5,
                BreakDuration = TimeSpan.FromMinutes(1)
            });

            builder.AddTimeout(attemptTimeout);
        });

        services.AddSingleton<OnevoApiClient>();
        services.AddSingleton<EnrollmentCoordinator>();

        services.AddHostedService<AgentWorker>();
        services.AddHostedService<ActivitySyncService>();
        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<TokenRefreshService>();
        // Registered right after TokenRefreshService per the Task 3 plan ("after token
        // refresh, before activity sync"). ActivitySyncService is left in its existing slot
        // above rather than reordered — DI registration order does not sequence independent
        // BackgroundServices (each runs its own ExecuteAsync as soon as the host starts), and
        // reordering it was not requested. PolicySyncService's own "before activity sync" is
        // achieved instead by its immediate-fetch-on-JWT behavior (see PolicySyncService.cs).
        services.AddHostedService<PolicySyncService>();
        // packages-guide §1 — SignalR remote commands (waits for JWT)
        services.AddHostedService<AgentCommandListener>();
    })
    .Build();

await host.RunAsync();
