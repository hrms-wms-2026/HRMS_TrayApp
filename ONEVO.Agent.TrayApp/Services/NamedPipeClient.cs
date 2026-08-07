namespace ONEVO.Agent.TrayApp.Services;

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class NamedPipeClient : INamedPipeClient, IAsyncDisposable
{
    private readonly ILogger<NamedPipeClient> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pending = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    // CRITICAL: all collectors MUST stop on this event (§2.3)
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus { get; private set; }
    public AgentPolicy? LastKnownPolicy { get; private set; }

    public NamedPipeClient(ILogger<NamedPipeClient> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ConnectWithRetryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < Constants.ReconnectMaxAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".", Constants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(Constants.IpcConnectionTimeoutMs, ct);
                await AuthenticateAsync(_pipe, ct);

                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                _writer = new StreamWriter(_pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                _logger.LogInformation("Connected to ONEVO Agent Service");

                await RequestStatusAsync(ct);
                await ReadLoopAsync(_pipe, utf8, ct);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning("Connect attempt {Attempt}/{Max} failed: {Msg}",
                    attempt + 1, Constants.ReconnectMaxAttempts, ex.Message);
                _pipe?.Dispose();
                _pipe = null;
                _writer = null;

                if (attempt < Constants.ReconnectMaxAttempts - 1)
                {
                    var delayMs = Constants.ReconnectBaseDelayMs * (1 << attempt);
                    delayMs += Random.Shared.Next(0, 500);
                    await Task.Delay(delayMs, ct);
                }
            }
        }
        _logger.LogError("Failed to connect to Service after {Max} attempts", Constants.ReconnectMaxAttempts);
        OnDisconnected?.Invoke();
    }

    private static async Task AuthenticateAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Constants.IpcConnectionTimeoutMs);

        var line = await reader.ReadLineAsync(cts.Token)
            ?? throw new InvalidOperationException("Pipe closed during auth challenge");

        var challenge = JsonSerializer.Deserialize<IpcEnvelope>(line)
            ?? throw new InvalidOperationException("Invalid auth challenge envelope");

        if (challenge.Type != IpcMessageTypes.NonceChallenge)
            throw new InvalidOperationException($"Unexpected message type: {challenge.Type}");

        var payload = challenge.Payload?.Deserialize<NonceChallengePayload>()
            ?? throw new InvalidOperationException("Missing nonce in challenge");

        var response = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            CorrelationId = challenge.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(payload.Nonce))
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        await writer.FlushAsync(ct);
    }

    private async Task RequestStatusAsync(CancellationToken ct)
    {
        if (_writer is null) return;
        var request = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
        await WriteEnvelopeAsync(request, ct);
    }

    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) =>
        WriteEnvelopeAsync(envelope, ct);

    public async Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action,
        CancellationToken ct,
        string? breakReason = null)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.LifecycleCommand,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new LifecycleCommandPayload(action, breakReason))
            };
            await WriteEnvelopeAsync(envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            IpcEnvelope reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Lifecycle {Action} timed out waiting for result", action);
                return null;
            }

            return reply.Payload?.Deserialize<LifecycleResultPayload>();
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    public async Task SubmitCollectionRecordsAsync(
        IReadOnlyList<CollectionRecord> records,
        CancellationToken ct)
    {
        if (records.Count == 0 || _writer is null)
            return;

        var envelope = new IpcEnvelope
        {
            Type = IpcMessageTypes.CollectionRecordSubmit,
            Payload = JsonSerializer.SerializeToElement(
                new CollectionRecordSubmitPayload { Records = records })
        };
        await WriteEnvelopeAsync(envelope, ct);
    }

    private async Task WriteEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_writer is null)
                return;
            await _writer.WriteLineAsync(JsonSerializer.Serialize(envelope).AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, Encoding utf8, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (line.Length > Constants.MaxMessageLengthBytes) continue;

                IpcEnvelope? envelope;
                try { envelope = JsonSerializer.Deserialize<IpcEnvelope>(line); }
                catch (JsonException) { continue; }

                if (envelope is null)
                    continue;

                // Complete any pending request/response pair first.
                if (!string.IsNullOrEmpty(envelope.CorrelationId)
                    && _pending.TryGetValue(envelope.CorrelationId, out var pending)
                    && envelope.Type is IpcMessageTypes.LifecycleResult
                        or IpcMessageTypes.StatusResponse
                        or IpcMessageTypes.CollectionRecordAck
                        or IpcMessageTypes.EnrollmentResult)
                {
                    pending.TrySetResult(envelope);
                }

                switch (envelope.Type)
                {
                    case IpcMessageTypes.StatusResponse:
                    {
                        var status = envelope.Payload?.Deserialize<StatusResponsePayload>();
                        if (status is not null)
                        {
                            LastKnownStatus = status;
                            OnStatusReceived?.Invoke(status);
                            OnStateReceived?.Invoke(status.State);
                        }
                        break;
                    }
                    case IpcMessageTypes.LifecycleResult:
                    {
                        // Also surface state from lifecycle result for navigation.
                        var result = envelope.Payload?.Deserialize<LifecycleResultPayload>();
                        if (result is not null)
                        {
                            OnStateReceived?.Invoke(result.State);
                            if (result.Session is not null)
                            {
                                var synth = new StatusResponsePayload(
                                    result.State, DateTimeOffset.UtcNow, result.Session);
                                LastKnownStatus = synth;
                                OnStatusReceived?.Invoke(synth);
                            }
                        }
                        break;
                    }
                    case IpcMessageTypes.PolicyPush:
                    {
                        var policyPayload = envelope.Payload?.Deserialize<PolicyPushPayload>();
                        if (policyPayload?.Policy is not null)
                        {
                            LastKnownPolicy = policyPayload.Policy;
                            OnPolicyReceived?.Invoke(policyPayload.Policy);
                        }
                        break;
                    }
                    case IpcMessageTypes.CollectionRecordAck:
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("IPC read loop ended: {Msg}", ex.Message);
        }
        finally
        {
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();
            OnDisconnected?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        _writer?.Dispose();
        _pipe?.Dispose();
        _writeLock.Dispose();
    }
}
