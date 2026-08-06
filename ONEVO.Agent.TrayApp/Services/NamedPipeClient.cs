namespace ONEVO.Agent.TrayApp.Services;

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
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    // CRITICAL: all collectors MUST stop on this event (§2.3)
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

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

    /// <summary>
    /// Hands privacy-scrubbed collection records to the Service. Tray never uploads to backend.
    /// </summary>
    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) =>
        WriteEnvelopeAsync(envelope, ct);

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

                switch (envelope.Type)
                {
                    case IpcMessageTypes.StatusResponse:
                    {
                        var status = envelope.Payload?.Deserialize<StatusResponsePayload>();
                        if (status is not null)
                            OnStateReceived?.Invoke(status.State);
                        break;
                    }
                    case IpcMessageTypes.PolicyPush:
                    {
                        var policyPayload = envelope.Payload?.Deserialize<PolicyPushPayload>();
                        if (policyPayload?.Policy is not null)
                            OnPolicyReceived?.Invoke(policyPayload.Policy);
                        break;
                    }
                    case IpcMessageTypes.CollectionRecordAck:
                        // Acks are fire-and-forget for now; future: track pending event IDs.
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
