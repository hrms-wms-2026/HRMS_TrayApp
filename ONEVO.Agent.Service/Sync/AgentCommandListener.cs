namespace ONEVO.Agent.Service.Sync;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;

/// <summary>
/// packages-guide §1 / Week 3: SignalR client for server → agent commands.
/// Starts only when ApiBaseUrl + Device JWT are present. Handlers are safe no-ops
/// until the backend hub contract is finalized.
/// </summary>
public sealed class AgentCommandListener : BackgroundService
{
    private readonly ILogger<AgentCommandListener> _logger;
    private readonly IOptions<AgentOptions> _options;
    private readonly CredentialStore _credentials;
    private readonly AgentStateMachine _stateMachine;
    private HubConnection? _connection;

    public AgentCommandListener(
        ILogger<AgentCommandListener> logger,
        IOptions<AgentOptions> options,
        CredentialStore credentials,
        AgentStateMachine stateMachine)
    {
        _logger = logger;
        _options = options;
        _credentials = credentials;
        _stateMachine = stateMachine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!TryBuildConnection())
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                _connection!.Closed += async error =>
                {
                    _logger.LogWarning(error, "SignalR connection closed");
                    await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                };

                RegisterHandlers();

                await _connection.StartAsync(stoppingToken);
                _logger.LogInformation(
                    "SignalR connected to agent-commands hub (state={State})",
                    _stateMachine.CurrentState);

                // Stay connected until cancelled or disconnect loops via AutomaticReconnect.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR not available yet — will retry");
                await SafeDisposeConnectionAsync();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        await SafeDisposeConnectionAsync();
    }

    private bool TryBuildConnection()
    {
        var baseUrl = _options.Value.ApiBaseUrl?.TrimEnd('/');
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("SignalR skipped — ApiBaseUrl or Device JWT missing");
            return false;
        }

        var hubUrl = $"{baseUrl}/hubs/agent-commands";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = () =>
                    Task.FromResult<string?>(_credentials.ReadDeviceJwt());
            })
            .WithAutomaticReconnect()
            .Build();

        return true;
    }

    private void RegisterHandlers()
    {
        if (_connection is null) return;

        // Contract from packages-guide — lifecycle is still owned by PresenceSession/IPC.
        _connection.On<object>("StartMonitoring", _ =>
            _logger.LogInformation("SignalR StartMonitoring received (state={State})", _stateMachine.CurrentState));

        _connection.On<object>("StopMonitoring", _ =>
            _logger.LogInformation("SignalR StopMonitoring received"));

        _connection.On<object>("PauseMonitoring", _ =>
            _logger.LogInformation("SignalR PauseMonitoring received"));

        _connection.On<object>("ResumeMonitoring", _ =>
            _logger.LogInformation("SignalR ResumeMonitoring received"));

        _connection.On<object>("RefreshPolicy", _ =>
            _logger.LogInformation("SignalR RefreshPolicy received"));

        _connection.On<object>("ExecuteCommand", _ =>
            _logger.LogInformation("SignalR ExecuteCommand received"));
    }

    private async Task SafeDisposeConnectionAsync()
    {
        if (_connection is null) return;
        try
        {
            await _connection.DisposeAsync();
        }
        catch { /* ignore */ }
        _connection = null;
    }
}
