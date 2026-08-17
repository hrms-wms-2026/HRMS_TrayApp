namespace ONEVO.Agent.TrayApp.Collectors;

using System.Linq;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Starts/stops interactive collectors from monitoring state + policy.
/// Collectors never self-start. IPC loss stops all capture immediately.
/// </summary>
public sealed class CollectorCoordinator : ICollectorLifecycleCoordinator, IAsyncDisposable
{
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    /// <summary>
    /// Used when Service sends Active before PolicyPush arrives (or policy message is missed).
    /// Activity is on; other collectors stay off until real policy arrives — this MUST keep
    /// InactivityScreenshotEnabled false, since there is no real, version-checked policy behind it.
    /// </summary>
    private static readonly AgentPolicy LocalDefaultPolicy = new()
    {
        Version = "tray-local-default",
        ActivitySignalEnabled = true,
        AppUsageEnabled = true,
        ScreenshotEnabled = false,
        CameraVerificationEnabled = false,
        InactivityScreenshotEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddDays(1)
    };

    private readonly ILogger<CollectorCoordinator> _logger;
    private readonly IEnumerable<IAgentCollector> _collectors;
    private readonly INamedPipeClient _pipeClient;
    private readonly NotificationService _notificationService;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    private AgentPolicy? _policy;
    private MonitoringState _state = MonitoringState.Unenrolled;
    private bool _collectorsRunning;
    private string? _appliedPolicyVersion;
    private CancellationTokenSource? _runCts;

    /// <summary>
    /// Collectors that confirmed <see cref="IAgentCollector.IsRunning"/> immediately after their
    /// own <see cref="IAgentCollector.StartAsync"/> succeeded during the current run — i.e. were
    /// actually eligible under the policy they were started with, as opposed to a collector whose
    /// own internal policy gate declined to run at all. Only members of this set are checked for a
    /// later stall (see <see cref="ReconcileAsync"/>), so a collector that is intentionally,
    /// permanently not running under the current policy (e.g. InactivityScreenshotEnabled=false)
    /// never falsely triggers a restart loop.
    /// </summary>
    private readonly HashSet<IAgentCollector> _confirmedRunning = new();

    public CollectorCoordinator(
        ILogger<CollectorCoordinator> logger,
        IEnumerable<IAgentCollector> collectors,
        INamedPipeClient pipeClient,
        NotificationService notificationService)
    {
        _logger = logger;
        _collectors = collectors;
        _pipeClient = pipeClient;
        _notificationService = notificationService;

        _pipeClient.OnStateReceived += OnStateReceived;
        _pipeClient.OnPolicyReceived += OnPolicyReceived;
        _pipeClient.OnNotificationReceived += OnWellnessNotificationReceived;
        _pipeClient.OnDisconnected += OnDisconnected;
    }

    private static void BootLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLogPath)!);
            File.AppendAllText(BootLogPath, $"{DateTimeOffset.Now:O} [Coordinator] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    private void OnStateReceived(MonitoringState state)
    {
        lock (_gate)
        {
            _state = state;
        }
        BootLog($"State={state}");
        _ = ReconcileAsync();
    }

    private void OnPolicyReceived(AgentPolicy policy)
    {
        lock (_gate)
        {
            _policy = policy;
        }
        BootLog($"Policy received Version={policy.Version} Activity={policy.ActivitySignalEnabled}");
        _ = ReconcileAsync();
    }

    private void OnWellnessNotificationReceived(NotificationPushPayload payload)
    {
        if (string.Equals(payload.Type, "LongIdleAlert", StringComparison.OrdinalIgnoreCase))
            _notificationService.ShowWarning(payload.Title, payload.Message);
        else
            _notificationService.ShowInfo(payload.Title, payload.Message);
    }

    private void OnDisconnected()
    {
        BootLog("IPC disconnected — stop collectors");
        _logger.LogWarning("IPC disconnected — stopping all collectors immediately");
        _ = StopAllAsync();
    }

    private async Task ReconcileAsync()
    {
        await _reconcileLock.WaitAsync();
        try
        {
            MonitoringState state;
            AgentPolicy? policy;
            lock (_gate)
            {
                state = _state;
                policy = _policy;
            }

            // Phase-1: if Active but policy not yet received, use local default
            // so keyboard/mouse capture can start immediately after auth.
            var effective = policy
                ?? (state == MonitoringState.Active ? LocalDefaultPolicy : null);

            var shouldRun = state == MonitoringState.Active
                && effective is not null
                && effective.ActivitySignalEnabled;

            BootLog($"Reconcile state={state} hasPolicy={policy is not null} shouldRun={shouldRun}");

            if (shouldRun)
            {
                bool versionChanged;
                bool anyStalled;
                lock (_gate)
                {
                    versionChanged = _collectorsRunning && _appliedPolicyVersion != effective!.Version;
                    // A collector that confirmed it started can later self-stop internally (e.g.
                    // InactivityScreenshotCollector's own ValidUntil staleness check) without the
                    // coordinator ever being told to Stop. Because PolicySyncService only broadcasts
                    // when the policy VERSION changes — not merely because ValidUntil was refreshed
                    // — an outage that outlasts the policy TTL can otherwise leave that collector
                    // dark until an unrelated state transition happens to restart everything. Treat
                    // a stalled, previously-confirmed collector the same as a version change.
                    anyStalled = _collectorsRunning && _confirmedRunning.Any(c => !c.IsRunning);
                }

                if (versionChanged || anyStalled)
                {
                    BootLog(anyStalled
                        ? "A collector self-stopped while it should still be running — restarting collectors"
                        : $"Policy version changed ({_appliedPolicyVersion} -> {effective!.Version}) — restarting collectors");
                    await StopAllAsync();
                }

                await StartAllAsync(effective!);
            }
            else
            {
                await StopAllAsync();
            }
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task StartAllAsync(AgentPolicy policy)
    {
        lock (_gate)
        {
            if (_collectorsRunning)
                return;
            _collectorsRunning = true;
            _appliedPolicyVersion = policy.Version;
            _runCts = new CancellationTokenSource();
        }

        var ct = _runCts!.Token;
        foreach (var collector in _collectors)
        {
            try
            {
                BootLog($"Starting collector {collector.Name}");
                await collector.StartAsync(policy, ct);
                BootLog($"Started collector {collector.Name}");
                _logger.LogInformation("Collector started: {Name}", collector.Name);

                // Only remember collectors that actually confirm they're running under this policy
                // — a collector whose own internal gate declined to start (e.g. a feature flag off)
                // must never be treated as "stalled" later; see _confirmedRunning's doc comment.
                if (collector.IsRunning)
                    lock (_gate) _confirmedRunning.Add(collector);
            }
            catch (Exception ex)
            {
                BootLog($"Start failed {collector.Name}: {ex}");
                _logger.LogError(ex, "Failed to start collector {Name}", collector.Name);
            }
        }
    }

    private async Task StopAllAsync()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_collectorsRunning)
                return;
            _collectorsRunning = false;
            _appliedPolicyVersion = null;
            _confirmedRunning.Clear();
            cts = _runCts;
            _runCts = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        foreach (var collector in _collectors)
        {
            try
            {
                await collector.StopAsync(CancellationToken.None);
                BootLog($"Stopped collector {collector.Name}");
            }
            catch (Exception ex)
            {
                BootLog($"Stop failed {collector.Name}: {ex.Message}");
                _logger.LogError(ex, "Failed to stop collector {Name}", collector.Name);
            }
        }
    }

    /// <summary>
    /// <see cref="ICollectorLifecycleCoordinator.PrepareForPauseAsync"/> — stops all collectors
    /// (each collector's own <c>StopAsync</c> is responsible for draining any in-flight prompt or
    /// capture + attempt submission) ahead of a pausing lifecycle command. Serialized against
    /// <see cref="ReconcileAsync"/> via <see cref="_reconcileLock"/> so a concurrently arriving
    /// state/policy push cannot race this drain and restart collectors underneath it.
    /// </summary>
    public async Task PrepareForPauseAsync(CancellationToken ct)
    {
        await _reconcileLock.WaitAsync(ct);
        try
        {
            await StopAllAsync();
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    /// <summary>
    /// <see cref="ICollectorLifecycleCoordinator.ResumeAfterRejectedPauseAsync"/> — re-reconciles
    /// against the current state/policy, which restarts collectors if the authoritative state is
    /// still <see cref="MonitoringState.Active"/>.
    /// </summary>
    public Task ResumeAfterRejectedPauseAsync(CancellationToken ct) => ReconcileAsync();

    public async ValueTask DisposeAsync()
    {
        _pipeClient.OnStateReceived -= OnStateReceived;
        _pipeClient.OnPolicyReceived -= OnPolicyReceived;
        _pipeClient.OnNotificationReceived -= OnWellnessNotificationReceived;
        _pipeClient.OnDisconnected -= OnDisconnected;
        await StopAllAsync();
        _reconcileLock.Dispose();
    }
}
