namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.TrayApp.Capture;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Replaces the old always-on <c>ScreenshotCollector</c>. Watches continuous keyboard/mouse
/// inactivity; at each configured idle boundary, prompts the employee via
/// <see cref="IInactivityPromptService"/>, captures via <see cref="IScreenshotCaptureService"/> only
/// on Allow, and submits exactly one <see cref="InactivityCaptureAttemptPayload"/> (metadata-only,
/// or with JPEG bytes for a <c>captured</c> outcome) over IPC for every continuous-idle bucket
/// (§7.5).
/// </summary>
/// <remarks>
/// <para>
/// State machine intent (design spec "Inactivity State Machine"): Stopped → Observing →
/// PromptPending(attemptId, bucket) → Capturing/Declined/TimedOut/Cancelled → Observing. The
/// five-second poll loop (<see cref="PollLoopAsync"/>, the untestable shell) samples
/// <see cref="IIdleTimeProvider"/> and drives the testable core, <see cref="EvaluateAsync"/>, which
/// computes a bucket from the policy threshold and prompts at most once per continuous idle period.
/// A <see cref="SemaphoreSlim"/> (<see cref="_lock"/>) serializes bucket-transition decisions
/// against the currently pending prompt/capture workflow so a timer tick and a notification-driven
/// activity-resumed cancellation can never both start/finish a second concurrent attempt for the
/// same period.
/// </para>
/// <para>
/// Policy staleness: <see cref="AgentPolicy.ValidUntil"/> is re-checked against the current wall
/// clock on every <see cref="EvaluateAsync"/> tick (mirroring
/// <c>ONEVO.Agent.Service.Policy.PolicyCache.Current</c>'s live-degrade-on-read pattern), not only
/// at <see cref="StartAsync"/> — <c>PolicySyncService</c> only broadcasts a new
/// <c>PolicyPush</c> when the policy version changes, never purely because it expired, so a stale
/// cached policy that still says <c>InactivityScreenshotEnabled: true</c> must not keep prompting
/// once its validity window has passed.
/// </para>
/// </remarks>
public sealed class InactivityScreenshotCollector : IAgentCollector
{
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    private readonly ILogger<InactivityScreenshotCollector> _logger;
    private readonly IIdleTimeProvider _idleTimeProvider;
    private readonly IInactivityPromptService _promptService;
    private readonly IScreenshotCaptureService _captureService;
    private readonly INamedPipeClient _pipeClient;
    private readonly TimeSpan _pollInterval;

    // Guards _started/_running/_policy/_loopCts/_loopTask — quick, synchronous bookkeeping only.
    private readonly object _gate = new();
    private bool _started;
    private bool _running;
    private AgentPolicy? _policy;
    private int _idleThresholdSeconds;
    private int _promptExpirySeconds;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // Guards bucket transition / prompt / capture / stop, per the design spec: only one attempt
    // workflow may be in flight at a time, and a concurrent tick or Stop must be able to observe
    // and cancel it cleanly.
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _lastPromptedBucket;
    private int _lastIdleSeconds;
    private Guid? _pendingAttemptId;
    private CancellationTokenSource? _pendingCts;
    private int _pendingIdleAtStart;
    private Task? _workflowTask;

    public string Name => "InactivityScreenshot";

    /// <summary>True while the collector is actively evaluating idle buckets (test/diagnostic seam).</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public InactivityScreenshotCollector(
        ILogger<InactivityScreenshotCollector> logger,
        IIdleTimeProvider idleTimeProvider,
        IInactivityPromptService promptService,
        IScreenshotCaptureService captureService,
        INamedPipeClient pipeClient,
        TimeSpan? pollInterval = null)
    {
        _logger = logger;
        _idleTimeProvider = idleTimeProvider;
        _promptService = promptService;
        _captureService = captureService;
        _pipeClient = pipeClient;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.ActivitySignalEnabled || !policy.ScreenshotEnabled || !policy.InactivityScreenshotEnabled)
        {
            _logger.LogDebug("{Name}: policy disabled — not starting", Name);
            return Task.CompletedTask;
        }

        if (IsExpired(policy, DateTimeOffset.UtcNow))
        {
            _logger.LogWarning(
                "{Name}: policy already expired (ValidUntil={ValidUntil}) — refusing to start", Name, policy.ValidUntil);
            return Task.CompletedTask;
        }

        CancellationTokenSource loopCts;
        lock (_gate)
        {
            if (_started)
                return Task.CompletedTask; // idempotent — coordinator must Stop then Start to apply a new policy

            _started = true;
            _running = true;
            _policy = policy;
            _idleThresholdSeconds = policy.IdleThresholdMinutes * 60;
            // Same 90% ratio the old fixed 300s/270s pair used: the expiry window must end
            // before the next bucket boundary, or a slow-to-respond employee could see two
            // prompts stack while still inside a single continuous idle period.
            _promptExpirySeconds = (int)(_idleThresholdSeconds * 0.9);
            _lastPromptedBucket = 0;
            _lastIdleSeconds = 0;
            loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopCts = loopCts;
        }

        _loopTask = PollLoopAsync(loopCts.Token);
        _logger.LogInformation("{Name}: started (policy {Version})", Name, policy.Version);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? loopCts;
        Task? loopTask;
        lock (_gate)
        {
            if (!_started)
                return; // idempotent — deliberately keyed on _started, not _running: a policy that
                        // expired mid-run (EvaluateAsync sets _running=false) must still have its
                        // loop cancelled/drained here, or a later restart would leave two loops alive.
            _started = false;
            _running = false;
            loopCts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = null;
        }

        if (loopCts is not null)
            await loopCts.CancelAsync().ConfigureAwait(false);

        // Drain any in-flight bucket workflow: dismiss its prompt and cancel it so it unwinds
        // promptly (mapped to monitoring_stopped) instead of waiting out the full prompt-expiry
        // window, then wait for its attempt submission (including the Named Pipe write) to finish.
        CancellationTokenSource? pendingCts;
        Task? workflow;
        Guid? pendingId;
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            pendingCts = _pendingCts;
            workflow = _workflowTask;
            pendingId = _pendingAttemptId;
        }
        finally
        {
            _lock.Release();
        }

        if (pendingId is { } id)
            _promptService.Dismiss(id);
        pendingCts?.Cancel();

        if (workflow is not null)
        {
            try { await workflow.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None).ConfigureAwait(false); }
            catch { /* best-effort drain — never block the coordinator's Stop indefinitely */ }
        }

        if (loopTask is not null)
        {
            try { await loopTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore — loop already cancelled above */ }
        }

        loopCts?.Dispose();
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_pollInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var idleSeconds = _idleTimeProvider.GetIdleSeconds();
                await EvaluateAsync(idleSeconds, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Testable core of the bucket state machine. Computes the current bucket from the configured
    /// threshold and, when a new bucket boundary is crossed for the first time this continuous idle
    /// period, runs exactly one prompt/capture/submit workflow to completion before returning. A
    /// tick that arrives while a workflow is already pending only checks whether idle time has
    /// dropped (new input observed) and, if so, cancels that pending workflow — it never starts a
    /// second one.
    /// </summary>
    public async Task EvaluateAsync(int idleSeconds, DateTimeOffset now, CancellationToken ct)
    {
        bool running;
        AgentPolicy? policy;
        lock (_gate) { running = _running; policy = _policy; }

        if (!running || policy is null)
            return;

        if (IsExpired(policy, now))
        {
            _logger.LogWarning(
                "{Name}: cached policy expired at {ValidUntil} (now {Now}) — pausing evaluation until restarted",
                Name, policy.ValidUntil, now);
            lock (_gate) { _running = false; }
            return;
        }

        Task? toAwait = null;
        CancellationTokenSource? toCancel = null;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pendingAttemptId is not null)
            {
                // A workflow is already in flight for the current continuous-idle period. Only
                // react if idle time dropped — real new input, including the notification click
                // itself updating Windows' last-input time — by cancelling it; never start a
                // second concurrent workflow (design spec: "one attempt cannot capture twice").
                if (idleSeconds < _pendingIdleAtStart)
                {
                    toCancel = _pendingCts;

                    // Record the reset now, even though the cancelled workflow is still unwinding
                    // (its own finally clears _pendingAttemptId separately). Without this, the next
                    // tick after the workflow clears the pending state would still compare against
                    // the stale pre-interruption _lastIdleSeconds/_lastPromptedBucket and could
                    // silently swallow a legitimate fresh prompt at the same bucket boundary.
                    _lastIdleSeconds = idleSeconds;
                    _lastPromptedBucket = 0;
                }
            }
            else
            {
                if (idleSeconds < _lastIdleSeconds)
                    _lastPromptedBucket = 0; // new input outside a pending prompt -> new period
                _lastIdleSeconds = idleSeconds;

                var bucket = idleSeconds / _idleThresholdSeconds;
                if (bucket == 0)
                {
                    _lastPromptedBucket = 0;
                }
                else if (bucket > _lastPromptedBucket)
                {
                    _lastPromptedBucket = bucket;

                    var attemptId = Guid.NewGuid();
                    var workflowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _pendingAttemptId = attemptId;
                    _pendingCts = workflowCts;
                    _pendingIdleAtStart = idleSeconds;

                    var idleStartedAt = now - TimeSpan.FromSeconds(idleSeconds);
                    var workflow = RunPromptWorkflowAsync(
                        attemptId, policy.Version, idleStartedAt, TimeSpan.FromSeconds(idleSeconds), now,
                        workflowCts.Token);
                    _workflowTask = workflow;
                    toAwait = workflow;
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        toCancel?.Cancel();

        if (toAwait is not null)
            await toAwait.ConfigureAwait(false);
    }

    private async Task RunPromptWorkflowAsync(
        Guid attemptId,
        string policyVersion,
        DateTimeOffset idleStartedAt,
        TimeSpan idleFor,
        DateTimeOffset promptedAt,
        CancellationToken ct)
    {
        try
        {
            InactivityPromptDecision decision;
            try
            {
                decision = await _promptService.PromptAsync(
                        attemptId, idleFor, TimeSpan.FromSeconds(_promptExpirySeconds), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                bool stillRunning;
                lock (_gate) { stillRunning = _running; }
                decision = stillRunning
                    ? InactivityPromptDecision.ActivityResumed
                    : InactivityPromptDecision.MonitoringStopped;
            }

            var decisionAt = DateTimeOffset.UtcNow;
            string outcome;
            string? failureCode = null;
            var jpegBytes = ReadOnlyMemory<byte>.Empty;
            var monitorCount = 0;
            DateTimeOffset? capturedAt = null;
            string? contentType = null;
            string? sha256 = null;

            if (decision == InactivityPromptDecision.Allowed)
            {
                ScreenshotCaptureResult captureResult;
                try
                {
                    captureResult = await _captureService.CaptureAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Name}: capture threw for attempt {AttemptId}", Name, attemptId);
                    captureResult = new ScreenshotCaptureResult(
                        false, ReadOnlyMemory<byte>.Empty, null, 0, default, null,
                        ScreenshotFailureCodes.CaptureApiFailed);
                }

                monitorCount = captureResult.MonitorCount;
                if (captureResult.Success)
                {
                    outcome = InactivityCaptureOutcomes.Captured;
                    jpegBytes = captureResult.JpegBytes;
                    capturedAt = captureResult.CapturedAt ?? DateTimeOffset.UtcNow;
                    contentType = "image/jpeg";
                    sha256 = captureResult.Sha256;
                }
                else
                {
                    outcome = InactivityCaptureOutcomes.CaptureFailed;
                    failureCode = captureResult.FailureCode;
                }
            }
            else
            {
                outcome = decision switch
                {
                    InactivityPromptDecision.Declined => InactivityCaptureOutcomes.Declined,
                    InactivityPromptDecision.TimedOut => InactivityCaptureOutcomes.TimedOut,
                    InactivityPromptDecision.ActivityResumed => InactivityCaptureOutcomes.ActivityResumed,
                    _ => InactivityCaptureOutcomes.MonitoringStopped
                };
            }

            var attempt = new InactivityCaptureAttemptPayload
            {
                AttemptId = attemptId,
                PolicyVersion = policyVersion,
                IdleStartedAt = idleStartedAt,
                PromptedAt = promptedAt,
                DecisionAt = decisionAt,
                CapturedAt = capturedAt,
                IdleDurationSeconds = (int)idleFor.TotalSeconds,
                MonitorCount = monitorCount,
                Outcome = outcome,
                FailureCode = failureCode,
                ContentType = contentType,
                Sha256 = sha256
            };

            BootLog($"attempt {attemptId} outcome={outcome} idleFor={(int)idleFor.TotalSeconds}s failureCode={failureCode ?? "-"}");

            try
            {
                await _pipeClient.SubmitInactivityAttemptAsync(attempt, jpegBytes, CancellationToken.None)
                    .ConfigureAwait(false);
                BootLog($"attempt {attemptId} submitted to IPC");
            }
            catch (Exception ex)
            {
                BootLog($"attempt {attemptId} IPC submit failed: {ex.Message}");
                _logger.LogWarning(ex, "{Name}: failed to submit inactivity attempt {AttemptId}", Name, attemptId);
            }
        }
        finally
        {
            await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_pendingAttemptId == attemptId)
                {
                    _pendingCts?.Dispose();
                    _pendingAttemptId = null;
                    _pendingCts = null;
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private static bool IsExpired(AgentPolicy policy, DateTimeOffset now) => policy.ValidUntil <= now;

    private static void BootLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLogPath)!);
            File.AppendAllText(BootLogPath, $"{DateTimeOffset.Now:O} [InactivityScreenshot] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }
}
