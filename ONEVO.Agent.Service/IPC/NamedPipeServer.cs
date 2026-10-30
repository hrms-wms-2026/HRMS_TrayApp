namespace ONEVO.Agent.Service.IPC;

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

public sealed class NamedPipeServer : IAsyncDisposable, IIpcBroadcaster
{
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly NamedPipeAuthenticator _authenticator;
    private readonly IIpcClientVerifier _clientVerifier;
    private readonly ConcurrentDictionary<Guid, IpcConnection> _connections = new();
    private CancellationTokenSource? _cts;

    public event Func<IpcEnvelope, Func<IpcEnvelope, Task>, Task>? MessageReceived;

    public NamedPipeServer(
        ILogger<NamedPipeServer> logger,
        NamedPipeAuthenticator authenticator,
        IIpcClientVerifier clientVerifier)
    {
        _logger = logger;
        _authenticator = authenticator;
        _clientVerifier = clientVerifier;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation("Named Pipe server started on pipe: {Pipe}", Constants.PipeName);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreateSecurePipe();
                _logger.LogDebug("Waiting for IPC client on {Pipe}", Constants.PipeName);
                await pipe.WaitForConnectionAsync(ct);
                _logger.LogInformation("IPC client connected — authenticating");
                _ = HandleClientAsync(pipe, ct);
                pipe = null; // ownership transferred to HandleClientAsync
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                // Includes a failed secure-pipe create: retry it, never fall back to a default-ACL pipe.
                _logger.LogError(ex, "Pipe accept failed — retrying");
                pipe?.Dispose();
                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Internal (not private) so tests can drive a real, uniquely-named pipe pair straight
    /// into this method — exercising the actual authentication/read-loop/connection-tracking
    /// code without going through <see cref="AcceptLoopAsync"/>'s fixed <see cref="Constants.PipeName"/>,
    /// which on a dev machine may already be bound by a running ONEVO Agent Service instance.
    /// See ONEVO.Agent.Service.Tests/IPC/NamedPipeServerBroadcastTests.cs.
    /// </summary>
    internal async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            // Before anything is sent: an untrusted client must not even get the handshake.
            if (!_clientVerifier.IsTrusted(pipe))
                return;

            var reader = new BoundedLineReader(pipe, Constants.MaxMessageLengthBytes);
            reader.OversizedLineSkipped += bytes =>
                _logger.LogWarning("Oversized IPC message discarded ({Bytes} bytes)", bytes);

            if (!await _authenticator.AuthenticateAsync(pipe, reader, ct))
            {
                _logger.LogWarning("IPC client failed authentication — connection dropped");
                return;
            }

            _logger.LogInformation("IPC client authenticated");

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var writer = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

            // Tracked so BroadcastAsync can reach this connection later, and so a broadcast
            // racing a request-reply on the same connection still serializes through one
            // SemaphoreSlim — two concurrent writers on the same pipe would otherwise
            // interleave partial JSON lines.
            var connection = new IpcConnection(writer);
            var connectionId = Guid.NewGuid();
            _connections[connectionId] = connection;

            Task SendAsync(IpcEnvelope envelope) => connection.SendAsync(envelope, ct);

            // Not disposed: a request still in flight when the connection ends releases its slot
            // afterwards, and SemaphoreSlim needs no disposal unless its wait handle is used.
            var independentSlots = new SemaphoreSlim(
                IpcDispatch.MaxConcurrentRequestsPerConnection, IpcDispatch.MaxConcurrentRequestsPerConnection);

            try
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    IpcEnvelope? envelope;
                    try { envelope = JsonSerializer.Deserialize<IpcEnvelope>(line); }
                    catch (JsonException) { continue; }

                    if (envelope is null || !IpcProtocolVersion.IsCompatible(envelope.Version)) continue;

                    var handler = MessageReceived;
                    if (handler is null) continue;

                    if (IpcDispatch.RunsConcurrently(envelope.Type))
                    {
                        // Off the read loop, so a slow backend call cannot hold back the messages
                        // behind it (AUD-008). Waiting for a slot here bounds the work per client.
                        await independentSlots.WaitAsync(ct);
                        _ = HandleIndependentRequestAsync(handler, envelope, SendAsync, independentSlots);
                    }
                    else
                    {
                        await handler(envelope, SendAsync);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation("IPC client disconnected: {Message}", ex.Message);
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
            }
        }
    }

    private async Task HandleIndependentRequestAsync(
        Func<IpcEnvelope, Func<IpcEnvelope, Task>, Task> handler,
        IpcEnvelope envelope,
        Func<IpcEnvelope, Task> reply,
        SemaphoreSlim slots)
    {
        try
        {
            // The handler's synchronous start (credential read, payload parsing) must not run on
            // the read loop either.
            await Task.Yield();
            await handler(envelope, reply);
        }
        catch (Exception ex)
        {
            // On the read loop this would have dropped the connection. Here only this request
            // fails; the tray's own timeout for it reports the failure.
            _logger.LogWarning(ex, "IPC {Type} handler failed", envelope.Type);
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>Sends <paramref name="envelope"/> to every currently-authenticated connection.</summary>
    public async Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        if (_connections.IsEmpty) return;

        var line = JsonSerializer.Serialize(envelope);
        foreach (var (connectionId, connection) in _connections)
        {
            try
            {
                await connection.SendRawAsync(line, ct);
            }
            catch (Exception ex)
            {
                // A dead connection is cleaned up by its own HandleClientAsync's finally
                // block once the read loop notices; broadcasting to the rest must not stop.
                _logger.LogDebug(ex, "Broadcast to connection {ConnectionId} failed", connectionId);
            }
        }
    }

    private static NamedPipeServerStream CreateSecurePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName: Constants.PipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    public async ValueTask DisposeAsync()
    {
        // The container disposes this instance twice: once as NamedPipeServer and once via the
        // IIpcBroadcaster factory registration that returns the same object.
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    /// <summary>
    /// One tracked connection's writer plus the lock that serializes every write to it —
    /// whether that write is a request-reply (via the MessageReceived callback) or a
    /// BroadcastAsync fan-out. Both paths go through <see cref="SendRawAsync"/> so a
    /// broadcast racing a reply on the same connection can never interleave JSON lines.
    ///
    /// Not IDisposable: SemaphoreSlim(1,1) never allocates a wait handle unless
    /// .AvailableWaitHandle is touched (it isn't, here), so there's nothing to release on
    /// removal — and skipping a Dispose() call here avoids racing an in-flight WaitAsync/Release
    /// on this same semaphore from a concurrent BroadcastAsync against a connection that's
    /// mid-teardown.
    /// </summary>
    private sealed class IpcConnection
    {
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public IpcConnection(StreamWriter writer) => _writer = writer;

        public Task SendAsync(IpcEnvelope envelope, CancellationToken ct) =>
            SendRawAsync(JsonSerializer.Serialize(envelope), ct);

        public async Task SendRawAsync(string line, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await _writer.WriteLineAsync(line.AsMemory(), ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
