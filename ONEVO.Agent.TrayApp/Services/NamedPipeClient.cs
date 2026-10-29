namespace ONEVO.Agent.TrayApp.Services;

using System.IO.Pipes;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

public sealed class NamedPipeClient : IAsyncDisposable
{
    private readonly ILogger<NamedPipeClient> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private readonly string _pipeName;

    // CRITICAL: all collectors MUST stop on this event (§2.3)
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public NamedPipeClient(ILogger<NamedPipeClient> logger)
        : this(logger, Constants.PipeName)
    {
    }

    internal NamedPipeClient(ILogger<NamedPipeClient> logger, string pipeName)
    {
        _logger = logger;
        _pipeName = pipeName;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ConnectWithRetryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs for the lifetime of the tray. The service can start after the tray (boot, delayed
    /// start) or restart (upgrade, crash), so the client never gives up (ISS-013).
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var connected = false;
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(Constants.IpcConnectionTimeoutMs, ct);

                // One reader/writer for the whole connection (ISS-006).
                using var reader = IpcLineCodec.CreateReader(_pipe);
                var writer = IpcLineCodec.CreateWriter(_pipe);
                await AuthenticateAsync(reader, writer, ct);

                _writer = writer;
                connected = true;
                failures = 0;
                _logger.LogInformation("Connected to ONEVO Agent Service");

                await RequestStatusAsync(ct);
                await ReadLoopAsync(_pipe, reader, ct); // raises OnDisconnected when it ends
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogWarning("Connect attempt {Attempt} failed: {Msg}", failures, ex.Message);
                if (failures == Constants.ReconnectMaxAttempts)
                {
                    _logger.LogError("Service unreachable after {Max} attempts; still retrying", failures);
                    OnDisconnected?.Invoke();
                }
            }
            finally
            {
                _writer = null;
                _pipe?.Dispose();
                _pipe = null;
            }

            var delayMs = connected
                ? Constants.ReconnectBaseDelayMs
                : Math.Min(Constants.ReconnectBaseDelayMs * (1 << Math.Min(failures - 1, 5)), Constants.ReconnectMaxDelayMs);
            delayMs += Random.Shared.Next(0, 500);
            try { await Task.Delay(delayMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task AuthenticateAsync(StreamReader reader, StreamWriter writer, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Constants.IpcConnectionTimeoutMs);

        var line = await reader.ReadLineAsync(cts.Token)
            ?? throw new InvalidOperationException("Pipe closed during auth challenge");

        if (!IpcLineCodec.TryParse(line, out var challenge))
            throw new InvalidOperationException("Invalid auth challenge envelope");

        if (challenge!.Type != IpcMessageTypes.NonceChallenge)
            throw new InvalidOperationException($"Unexpected message type: {challenge.Type}");

        var payload = challenge.Payload?.Deserialize<NonceChallengePayload>()
            ?? throw new InvalidOperationException("Missing nonce in challenge");

        var response = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceResponse,
            CorrelationId = challenge.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(payload.Nonce))
        };
        await writer.WriteLineAsync(IpcLineCodec.Serialize(response));
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
            await _writer.WriteLineAsync(IpcLineCodec.Serialize(envelope).AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (!IpcLineCodec.TryParse(line, out var envelope))
                    continue;

                switch (envelope!.Type)
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
