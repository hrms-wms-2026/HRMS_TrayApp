namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using System.Drawing;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Capture;
using ONEVO.Agent.TrayApp.Services;

public sealed class InactivityScreenshotCollectorTests
{
    private static AgentPolicy EnabledPolicy(DateTimeOffset? validUntil = null) => new()
    {
        Version = "v1",
        ActivitySignalEnabled = true,
        AppUsageEnabled = false,
        ScreenshotEnabled = true,
        InactivityScreenshotEnabled = true,
        CameraVerificationEnabled = false,
        ValidUntil = validUntil ?? DateTimeOffset.UtcNow.AddHours(1)
    };

    private readonly FakeInactivityPromptService _prompt = new();
    private readonly FakeScreenshotCaptureService _capture = new();
    private readonly RecordingPipeClient _pipe = new();
    private readonly InactivityScreenshotCollector _sut;

    public InactivityScreenshotCollectorTests()
    {
        _sut = new InactivityScreenshotCollector(
            NullLogger<InactivityScreenshotCollector>.Instance,
            new FixedIdleTimeProvider(),
            _prompt,
            _capture,
            _pipe,
            TimeSpan.FromHours(1)); // huge poll interval — tests drive EvaluateAsync directly
    }

    [Theory]
    [InlineData(299, 0)]
    [InlineData(300, 1)]
    [InlineData(599, 1)]
    [InlineData(600, 2)]
    public async Task Prompts_once_per_five_minute_bucket(int idleSeconds, int expected)
    {
        await _sut.StartAsync(EnabledPolicy(), default);
        if (idleSeconds >= 300)
            await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);
        if (idleSeconds >= 600)
            await _sut.EvaluateAsync(600, DateTimeOffset.Parse("2026-08-10T01:10:00Z"), default);
        Assert.Equal(expected, _prompt.RequestCount);
    }

    [Fact]
    public async Task Allow_CapturesScreenshot_AndSubmitsCapturedAttempt()
    {
        _prompt.NextDecision = InactivityPromptDecision.Allowed;
        _capture.NextResult = new ScreenshotCaptureResult(
            true, new byte[] { 1, 2, 3, 4 }, DateTimeOffset.Parse("2026-08-10T01:05:02Z"), 2,
            new Rectangle(-1920, 0, 3840, 1080), "abc123", null);

        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);

        Assert.Equal(1, _capture.CallCount);
        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.Captured, submitted.Attempt.Outcome);
        Assert.Equal(4, submitted.JpegLength);
        Assert.Equal(2, submitted.Attempt.MonitorCount);
        Assert.Equal("image/jpeg", submitted.Attempt.ContentType);
        Assert.Null(submitted.Attempt.FailureCode);
    }

    [Fact]
    public async Task Skip_SubmitsDeclinedAttempt_WithoutCapturing()
    {
        _prompt.NextDecision = InactivityPromptDecision.Declined;

        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);

        Assert.Equal(0, _capture.CallCount);
        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.Declined, submitted.Attempt.Outcome);
        Assert.Equal(0, submitted.JpegLength);
    }

    [Fact]
    public async Task Expiry_TimesOut_SubmitsTimedOutAttempt()
    {
        _prompt.NextDecision = InactivityPromptDecision.TimedOut;

        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);

        Assert.Equal(0, _capture.CallCount);
        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.TimedOut, submitted.Attempt.Outcome);
    }

    [Fact]
    public async Task CaptureFails_SubmitsCaptureFailedAttempt_WithFailureCode()
    {
        _prompt.NextDecision = InactivityPromptDecision.Allowed;
        _capture.NextResult = new ScreenshotCaptureResult(
            false, ReadOnlyMemory<byte>.Empty, null, 0, default, null, ScreenshotFailureCodes.NoDisplays);

        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);

        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.CaptureFailed, submitted.Attempt.Outcome);
        Assert.Equal(ScreenshotFailureCodes.NoDisplays, submitted.Attempt.FailureCode);
        Assert.Equal(0, submitted.JpegLength);
    }

    [Fact]
    public async Task InputReset_AllowsFreshPromptInNextContinuousIdlePeriod()
    {
        _prompt.NextDecision = InactivityPromptDecision.Declined;

        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);
        Assert.Equal(1, _prompt.RequestCount);

        // Employee interacts — idle counter resets well below the threshold.
        await _sut.EvaluateAsync(5, DateTimeOffset.Parse("2026-08-10T01:05:05Z"), default);
        Assert.Equal(1, _prompt.RequestCount); // no prompt for a sub-threshold tick

        // A brand-new continuous idle period crosses the first bucket again.
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:10:05Z"), default);
        Assert.Equal(2, _prompt.RequestCount);
    }

    [Fact]
    public async Task AllowVersusPollRace_ActivityDuringPendingPrompt_CancelsAndRecordsActivityResumed()
    {
        var gated = new GatedPromptService();
        var sut = new InactivityScreenshotCollector(
            NullLogger<InactivityScreenshotCollector>.Instance,
            new FixedIdleTimeProvider(),
            gated,
            _capture,
            _pipe,
            TimeSpan.FromHours(1));

        await sut.StartAsync(EnabledPolicy(), default);

        var t1 = DateTimeOffset.Parse("2026-08-10T01:05:00Z");
        var firstEvaluate = sut.EvaluateAsync(300, t1, default);

        // Deterministic handshake — wait for the prompt to actually be requested, no sleeps.
        await gated.Entered;

        // The next real poll tick observes reduced idle (employee interacted) while the prompt for
        // the first bucket is still pending — this must cancel it, not start a second workflow.
        await sut.EvaluateAsync(2, t1.AddSeconds(1), default);

        await firstEvaluate;

        Assert.Equal(1, gated.RequestCount);
        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.ActivityResumed, submitted.Attempt.Outcome);
        Assert.Equal(0, _capture.CallCount);
    }

    [Fact]
    public async Task AfterActivityResumedCancellation_ANewPromptFiresOnceIdleReturnsToThreshold()
    {
        // Regression: the bucket tracker must reflect the reset the moment the pending prompt is
        // cancelled for real new input, not only once the cancelled workflow finishes unwinding —
        // otherwise a fresh idle period reaching the same bucket boundary again would be silently
        // swallowed (bucket <= stale _lastPromptedBucket).
        var gated = new GatedPromptService();
        var sut = new InactivityScreenshotCollector(
            NullLogger<InactivityScreenshotCollector>.Instance,
            new FixedIdleTimeProvider(),
            gated,
            _capture,
            _pipe,
            TimeSpan.FromHours(1));

        await sut.StartAsync(EnabledPolicy(), default);

        var t1 = DateTimeOffset.Parse("2026-08-10T01:05:00Z");
        var firstEvaluate = sut.EvaluateAsync(300, t1, default);
        await gated.Entered;
        await sut.EvaluateAsync(2, t1.AddSeconds(1), default); // activity resumed -> cancel
        await firstEvaluate;

        Assert.Equal(InactivityCaptureOutcomes.ActivityResumed, _pipe.Submitted[0].Attempt.Outcome);

        // A brand-new continuous idle period climbs back to the same 300s bucket boundary. Reset
        // the gate's handshake so `Entered` reports this second request specifically, and only wait
        // for the prompt to be *requested* — resolving it fully isn't needed to prove the fix.
        gated.ResetEntered();
        _ = sut.EvaluateAsync(300, t1.AddMinutes(10), default);
        await gated.Entered;

        Assert.Equal(2, gated.RequestCount);
    }

    [Fact]
    public async Task PolicyDisabled_InactivityScreenshotEnabledFalse_NeverStartsOrPrompts()
    {
        var disabled = EnabledPolicy() with { InactivityScreenshotEnabled = false };

        await _sut.StartAsync(disabled, default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);

        Assert.False(_sut.IsRunning);
        Assert.Equal(0, _prompt.RequestCount);
        Assert.Empty(_pipe.Submitted);
    }

    [Fact]
    public async Task ExpiredPolicy_AtStartAsync_RefusesToRun()
    {
        var expired = EnabledPolicy(DateTimeOffset.UtcNow.AddMinutes(-5));

        await _sut.StartAsync(expired, default);
        await _sut.EvaluateAsync(300, DateTimeOffset.UtcNow, default);

        Assert.False(_sut.IsRunning);
        Assert.Equal(0, _prompt.RequestCount);
    }

    [Fact]
    public async Task ExpiredPolicy_DuringEvaluate_StopsPromptingThenRestartCleanlyResumesOnce()
    {
        // Regression: a self-expire must not orphan the internal poll loop — StopAsync is keyed on
        // "started", not "running", so a subsequent coordinator-driven Stop-then-Start (real policy
        // reconfiguration flow) leaves exactly one loop alive and exactly one new prompt fires.
        var early = EnabledPolicy(DateTimeOffset.Parse("2026-08-10T01:04:00Z"));
        await _sut.StartAsync(early, default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default); // now > ValidUntil

        Assert.Equal(0, _prompt.RequestCount);
        Assert.False(_sut.IsRunning);

        await _sut.StopAsync(default);

        var fresh = EnabledPolicy(DateTimeOffset.UtcNow.AddHours(1));
        await _sut.StartAsync(fresh, default);
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T02:05:00Z"), default);

        Assert.Equal(1, _prompt.RequestCount);
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public async Task StopAsync_MidPrompt_CancelsAndSubmitsMonitoringStoppedAttempt()
    {
        // Covers the "stop mid-workflow" scenario however it is triggered — break/clock-out
        // pre-stop drain, IPC loss, or a policy-disabling push all route through the same
        // coordinator -> collector.StopAsync() call.
        var gated = new GatedPromptService();
        var sut = new InactivityScreenshotCollector(
            NullLogger<InactivityScreenshotCollector>.Instance,
            new FixedIdleTimeProvider(),
            gated,
            _capture,
            _pipe,
            TimeSpan.FromHours(1));

        await sut.StartAsync(EnabledPolicy(), default);
        var t1 = DateTimeOffset.Parse("2026-08-10T01:05:00Z");
        var evaluateTask = sut.EvaluateAsync(300, t1, default);
        await gated.Entered;

        await sut.StopAsync(default);
        await evaluateTask;

        Assert.False(sut.IsRunning);
        var submitted = Assert.Single(_pipe.Submitted);
        Assert.Equal(InactivityCaptureOutcomes.MonitoringStopped, submitted.Attempt.Outcome);
        Assert.True(gated.Dismissed);
    }

    [Fact]
    public async Task SubmitInactivityAttemptAsync_Throws_DoesNotCrashWorkflow()
    {
        _prompt.NextDecision = InactivityPromptDecision.Declined;
        _pipe.ThrowOnSubmit = true;

        await _sut.StartAsync(EnabledPolicy(), default);
        var ex = await Record.ExceptionAsync(() =>
            _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default));

        Assert.Null(ex);
    }

    [Fact]
    public async Task StartAsync_CalledTwice_IsIdempotent()
    {
        await _sut.StartAsync(EnabledPolicy(), default);
        await _sut.StartAsync(EnabledPolicy() with { Version = "v2" }, default);

        Assert.True(_sut.IsRunning);
    }
}

// --- Test doubles (kept in this file so Task 6's exact scoped `git add` — which stages
// tests/ONEVO.Agent.TrayApp.Tests/Collectors but not tests/.../Fakes — stays sufficient to
// compile the suite). All live in the ONEVO.Agent.TrayApp.Tests.Collectors namespace so
// CollectorCoordinatorTests.cs (same folder/namespace) can reuse them too. ---

internal sealed class FixedIdleTimeProvider : IIdleTimeProvider
{
    public int GetIdleSeconds() => 0;
}

internal sealed class FakeInactivityPromptService : IInactivityPromptService
{
    public int RequestCount { get; private set; }
    public InactivityPromptDecision NextDecision { get; set; } = InactivityPromptDecision.Declined;
    public List<Guid> DismissedAttemptIds { get; } = [];

    public Task<InactivityPromptDecision> PromptAsync(
        Guid attemptId, TimeSpan idleFor, TimeSpan expiresIn, CancellationToken ct)
    {
        RequestCount++;
        return Task.FromResult(NextDecision);
    }

    public void Dismiss(Guid attemptId) => DismissedAttemptIds.Add(attemptId);
}

/// <summary>
/// Prompt fake that blocks until the caller resolves or cancels it — lets a test deterministically
/// observe "the prompt has been requested and is now pending" (via <see cref="Entered"/>) before
/// racing a second concurrent evaluation against it, with no sleeps/polling.
/// </summary>
internal sealed class GatedPromptService : IInactivityPromptService
{
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int RequestCount { get; private set; }
    public bool Dismissed { get; private set; }
    public Task Entered => _entered.Task;

    public Task<InactivityPromptDecision> PromptAsync(
        Guid attemptId, TimeSpan idleFor, TimeSpan expiresIn, CancellationToken ct)
    {
        RequestCount++;
        var tcs = new TaskCompletionSource<InactivityPromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        _entered.TrySetResult();
        return tcs.Task;
    }

    /// <summary>Re-arms <see cref="Entered"/> so a test can wait for a *subsequent* PromptAsync call.</summary>
    public void ResetEntered() => _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dismiss(Guid attemptId) => Dismissed = true;
}

internal sealed class FakeScreenshotCaptureService : IScreenshotCaptureService
{
    public int CallCount { get; private set; }
    public ScreenshotCaptureResult NextResult { get; set; } = new(
        true, new byte[] { 1 }, DateTimeOffset.UtcNow, 1, new Rectangle(0, 0, 100, 100), "hash", null);

    public Task<ScreenshotCaptureResult> CaptureAsync(CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}

/// <summary>Minimal <see cref="INamedPipeClient"/> fake that only observes evidence submission.</summary>
internal sealed class RecordingPipeClient : INamedPipeClient
{
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus => null;
    public AgentPolicy? LastKnownPolicy { get; set; }

    public List<(InactivityCaptureAttemptPayload Attempt, int JpegLength)> Submitted { get; } = [];
    public bool ThrowOnSubmit { get; set; }
    public bool NextAckAccepted { get; set; } = true;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct) => Task.CompletedTask;
    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) => Task.CompletedTask;

    public Task<LifecycleResultPayload?> SendLifecycleAsync(LifecycleAction action, CancellationToken ct, string? breakReason = null) =>
        Task.FromResult<LifecycleResultPayload?>(null);

    public Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct) =>
        Task.FromResult<EnrollmentResultPayload?>(null);

    public Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct) =>
        Task.FromResult<LogoutResultPayload?>(null);

    public Task<bool> SubmitInactivityAttemptAsync(
        InactivityCaptureAttemptPayload attempt, ReadOnlyMemory<byte> jpegBytes, CancellationToken ct)
    {
        if (ThrowOnSubmit)
            throw new InvalidOperationException("Simulated IPC submit failure");

        Submitted.Add((attempt, jpegBytes.Length));
        return Task.FromResult(NextAckAccepted);
    }

    // Unused by these tests — kept so the fake can raise pipe events if a future test needs it.
    internal void RaiseDisconnected() => OnDisconnected?.Invoke();
    internal void RaiseState(MonitoringState s) => OnStateReceived?.Invoke(s);
    internal void RaiseStatus(StatusResponsePayload s) => OnStatusReceived?.Invoke(s);
    internal void RaisePolicy(AgentPolicy p) => OnPolicyReceived?.Invoke(p);
}
