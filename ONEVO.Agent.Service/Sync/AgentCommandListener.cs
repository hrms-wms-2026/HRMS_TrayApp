namespace ONEVO.Agent.Service.Sync;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// SignalR client for server → agent pushes on <c>/hubs/agent-commands</c>. This connection is
/// what lets the pollers run slowly: the backend pushes <c>AttendanceChanged</c>,
/// <c>NotificationsPending</c> and <c>RefreshPolicy</c> when something actually changes, and
/// treats the live connection itself as tray presence. It therefore must never stay down:
/// reconnects retry forever, and a fully closed connection is rebuilt by the outer loop.
/// </summary>
public sealed class AgentCommandListener : BackgroundService
{
    // Mirrors TrayPushMessages on the backend.
    internal const string PushReady = "PushReady";
    internal const string AttendanceChanged = "AttendanceChanged";
    internal const string NotificationsPending = "NotificationsPending";
    internal const string RefreshPolicy = "RefreshPolicy";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromSeconds(15);

    private readonly ILogger<AgentCommandListener> _logger;
    private readonly IOptions<AgentOptions> _options;
    private readonly CredentialStore _credentials;
    private readonly AgentStateMachine _stateMachine;
    private readonly PolicySyncService _policySync;
    private readonly PushChannelState _push;
    private readonly AttendanceStatusSyncService? _attendanceSync;
    private readonly NotificationPollingService? _notificationPolling;
    private HubConnection? _connection;

    public AgentCommandListener(
        ILogger<AgentCommandListener> logger,
        IOptions<AgentOptions> options,
        CredentialStore credentials,
        AgentStateMachine stateMachine,
        PolicySyncService policySync,
        PushChannelState? push = null,
        AttendanceStatusSyncService? attendanceSync = null,
        NotificationPollingService? notificationPolling = null)
    {
        _logger = logger;
        _options = options;
        _credentials = credentials;
        _stateMachine = stateMachine;
        _policySync = policySync;
        _push = push ?? new PushChannelState();
        _attendanceSync = attendanceSync;
        _notificationPolling = notificationPolling;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!ShouldBeConnected() || !TryBuildConnection())
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                    continue;
                }

                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connection!.Closed += error =>
                {
                    // Only after automatic reconnect gave up (it doesn't, see ForeverRetryPolicy)
                    // or the server closed us — either way the outer loop builds a fresh one.
                    _push.SetActive(false);
                    _logger.LogWarning(error, "SignalR connection closed — rebuilding");
                    closed.TrySetResult();
                    return Task.CompletedTask;
                };
                _connection.Reconnecting += error =>
                {
                    _push.SetActive(false); // pollers fall back to fast cadence until PushReady again
                    _logger.LogInformation(error, "SignalR reconnecting");
                    return Task.CompletedTask;
                };

                RegisterHandlers();

                await _connection.StartAsync(stoppingToken);
                _logger.LogInformation(
                    "SignalR connected to agent-commands hub (state={State})", _stateMachine.CurrentState);

                // Stay connected until the connection dies for good or this tray stops being
                // allowed to hold one (unenrolled / locked / credentials cleared).
                while (!closed.Task.IsCompleted && ShouldBeConnected())
                    await Task.WhenAny(closed.Task, Task.Delay(StateCheckInterval, stoppingToken));
                stoppingToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SignalR not available yet — will retry");
                await SafeDelayAsync(RetryDelay, stoppingToken);
            }
            finally
            {
                _push.SetActive(false);
                await SafeDisposeConnectionAsync();
            }
        }
    }

    /// <summary>A locked or unenrolled tray must not count as "present" on the backend.</summary>
    private bool ShouldBeConnected() =>
        _stateMachine.CurrentState is not (MonitoringState.Unenrolled or MonitoringState.Locked)
        && !string.IsNullOrWhiteSpace(_credentials.ReadDeviceJwt());

    private bool TryBuildConnection()
    {
        var baseUrl = _options.Value.ApiBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogDebug("SignalR skipped — ApiBaseUrl missing");
            return false;
        }

        var hubUrl = $"{baseUrl}/hubs/agent-commands";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = () =>
                    Task.FromResult<string?>(_credentials.ReadDeviceJwt());
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        return true;
    }

    private void RegisterHandlers()
    {
        if (_connection is null) return;

        // Sent by the backend on every (re)connect. Until it arrives the pollers stay fast, so a
        // backend that doesn't push never leaves this tray on slow polling.
        _connection.On(PushReady, () =>
        {
            _push.SetActive(true);
            _logger.LogInformation("SignalR push active — pollers on slow fallback cadence");
            // Anything that changed while we were disconnected was never pushed: catch up once.
            _ = CatchUpAsync();
        });

        _connection.On(AttendanceChanged, () =>
        {
            _logger.LogInformation("SignalR AttendanceChanged received");
            _ = RunSafeAsync(async ct =>
            {
                if (_attendanceSync is not null) await _attendanceSync.SafePollAsync(ct);
                // A break ending can also raise "break allowance exceeded".
                if (_notificationPolling is not null) await _notificationPolling.SafePollAsync(ct);
            });
        });

        _connection.On(NotificationsPending, () =>
        {
            _logger.LogInformation("SignalR NotificationsPending received");
            _ = RunSafeAsync(async ct =>
            {
                if (_notificationPolling is not null) await _notificationPolling.SafePollAsync(ct);
            });
        });

        _connection.On(RefreshPolicy, async () =>
        {
            _logger.LogInformation("SignalR RefreshPolicy received");
            await HandleRefreshPolicyAsync(CancellationToken.None);
        });

        // Contract from packages-guide — lifecycle is still owned by PresenceSession/IPC.
        _connection.On<object>("StartMonitoring", _ =>
            _logger.LogInformation("SignalR StartMonitoring received (state={State})", _stateMachine.CurrentState));
        _connection.On<object>("StopMonitoring", _ =>
            _logger.LogInformation("SignalR StopMonitoring received"));
        _connection.On<object>("PauseMonitoring", _ =>
            _logger.LogInformation("SignalR PauseMonitoring received"));
        _connection.On<object>("ResumeMonitoring", _ =>
            _logger.LogInformation("SignalR ResumeMonitoring received"));
        _connection.On<object>("ExecuteCommand", _ =>
            _logger.LogInformation("SignalR ExecuteCommand received"));
    }

    internal Task HandleRefreshPolicyAsync(CancellationToken ct) =>
        _policySync.RefreshOnceAsync(_credentials.ReadDeviceJwt(), ct);

    private Task CatchUpAsync() => RunSafeAsync(async ct =>
    {
        await HandleRefreshPolicyAsync(ct);
        if (_attendanceSync is not null) await _attendanceSync.SafePollAsync(ct);
        if (_notificationPolling is not null) await _notificationPolling.SafePollAsync(ct);
    });

    private async Task RunSafeAsync(Func<CancellationToken, Task> work)
    {
        try
        {
            await work(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Push-triggered refresh failed — the fallback poll will catch up");
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { /* stopping */ }
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

    /// <summary>
    /// The default policy (0, 2, 10, 30 s) gives up after four attempts — then the tray would sit
    /// on slow polling with no pushes until restarted. Retry quickly at first, then every 30 s, forever.
    /// </summary>
    internal sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Initial =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount < Initial.Length
                ? Initial[retryContext.PreviousRetryCount]
                : TimeSpan.FromSeconds(30);
    }
}
