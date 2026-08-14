namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed class FakeNamedPipeClient : INamedPipeClient
{
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus { get; set; }
    public AgentPolicy? LastKnownPolicy { get; set; }

    public List<IReadOnlyList<CollectionRecord>> Submitted { get; } = [];
    public List<IpcEnvelope> SentEnvelopes { get; } = [];
    public List<LifecycleAction> LifecycleActions { get; } = [];

    /// <summary>Optional canned result for SendLifecycleAsync. Null = auto-success.</summary>
    public LifecycleResultPayload? NextLifecycleResult { get; set; }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct)
    {
        Submitted.Add(records);
        return Task.CompletedTask;
    }

    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        SentEnvelopes.Add(envelope);
        return Task.CompletedTask;
    }

    /// <summary>Optional canned enrollment result. Null = auto-success.</summary>
    public EnrollmentResultPayload? NextEnrollmentResult { get; set; }

    public Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.ActivationCodeSubmit,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new ActivationCodeSubmitPayload(code.Trim().ToUpperInvariant()))
        });

        if (NextEnrollmentResult is not null)
            return Task.FromResult<EnrollmentResultPayload?>(NextEnrollmentResult);

        return Task.FromResult<EnrollmentResultPayload?>(
            new EnrollmentResultPayload
            {
                Success = true,
                ErrorCode = null,
                EmployeeName = "Test Employee",
                EmployeeEmail = "test.employee@test.dev",
                EmployeeNumber = "EMP-TEST-01"
            });
    }

    public Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null)
    {
        LifecycleActions.Add(action);
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.LifecycleCommand,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new LifecycleCommandPayload(action, breakReason))
        });

        if (NextLifecycleResult is not null)
            return Task.FromResult<LifecycleResultPayload?>(NextLifecycleResult);

        var state = action switch
        {
            LifecycleAction.ClockIn    => MonitoringState.Active,
            LifecycleAction.StartBreak => MonitoringState.Paused,
            LifecycleAction.EndBreak   => MonitoringState.Active,
            LifecycleAction.ClockOut   => MonitoringState.Stopped,
            _                          => MonitoringState.Stopped
        };

        var now = DateTimeOffset.UtcNow;
        var session = new SessionSnapshot(
            ClockInAt: action == LifecycleAction.ClockOut || action == LifecycleAction.ClockIn || action == LifecycleAction.StartBreak || action == LifecycleAction.EndBreak
                ? now.AddHours(-1)
                : null,
            ClockOutAt: action == LifecycleAction.ClockOut ? now : null,
            IsOnBreak: action == LifecycleAction.StartBreak,
            CurrentBreakStartedAt: action == LifecycleAction.StartBreak ? now : null,
            AccumulatedBreak: TimeSpan.FromMinutes(10),
            AccumulatedWork: TimeSpan.FromHours(1),
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: action == LifecycleAction.StartBreak ? 1 : 0);

        if (action == LifecycleAction.ClockIn)
        {
            session = session with
            {
                ClockInAt = now,
                AccumulatedBreak = TimeSpan.Zero,
                AccumulatedWork = TimeSpan.Zero,
                BreakSessionCount = 0,
                IsOnBreak = false,
                CurrentBreakStartedAt = null,
                ClockOutAt = null
            };
        }

        return Task.FromResult<LifecycleResultPayload?>(
            new LifecycleResultPayload(true, null, "ok", state, session));
    }

    /// <summary>Optional canned logout result. Null = auto-success.</summary>
    public LogoutResultPayload? NextLogoutResult { get; set; }

    public Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope { Type = IpcMessageTypes.LogoutRequest });

        return Task.FromResult<LogoutResultPayload?>(
            NextLogoutResult ?? new LogoutResultPayload(true, null));
    }

    /// <summary>Optional canned result for StartBiometricEnrollmentAsync. Null = auto-success.</summary>
    public BiometricEnrollmentSessionReadyPayload? NextEnrollmentSessionResult { get; set; }

    public Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope { Type = IpcMessageTypes.BiometricEnrollmentStart });

        if (NextEnrollmentSessionResult is not null)
            return Task.FromResult<BiometricEnrollmentSessionReadyPayload?>(NextEnrollmentSessionResult);

        return Task.FromResult<BiometricEnrollmentSessionReadyPayload?>(new BiometricEnrollmentSessionReadyPayload(
            true, null, Guid.NewGuid(), "aws-session-fake", "ap-south-1", "FaceMovementAndLightChallenge",
            "AKIA", "secret", "token", DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    /// <summary>Optional canned result for CompleteBiometricEnrollmentAsync. Null = auto-success.</summary>
    public BiometricEnrollmentResultPayload? NextEnrollmentCompletionResult { get; set; }

    public Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.BiometricEnrollmentCaptureFinished,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new BiometricEnrollmentCaptureFinishedPayload(attemptId, captureSucceeded, clientErrorCode))
        });

        if (NextEnrollmentCompletionResult is not null)
            return Task.FromResult<BiometricEnrollmentResultPayload?>(NextEnrollmentCompletionResult);

        return Task.FromResult<BiometricEnrollmentResultPayload?>(
            new BiometricEnrollmentResultPayload(true, null, "active"));
    }

    public void SimulateDisconnect()              => OnDisconnected?.Invoke();
    public void SimulateState(MonitoringState s)  => OnStateReceived?.Invoke(s);
    public void SimulateStatus(StatusResponsePayload s)
    {
        OnStatusReceived?.Invoke(s);
        OnStateReceived?.Invoke(s.State);
    }
    public void SimulatePolicy(AgentPolicy p)     => OnPolicyReceived?.Invoke(p);
}
