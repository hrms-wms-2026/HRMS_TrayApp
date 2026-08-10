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
    private readonly ConcurrentDictionary<Guid, IpcConnection> _connections = new();
    private CancellationTokenSource? _cts;

    public event Func<IpcEnvelope, Func<IpcEnvelope, Task>, Task>? MessageReceived;

    public NamedPipeServer(ILogger<NamedPipeServer> logger, NamedPipeAuthenticator authenticator)
    {
        _logger = logger;
        _authenticator = authenticator;
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
                pipe = CreatePipe();
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
            if (!await _authenticator.AuthenticateAsync(pipe, ct))
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

            using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            try
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (line.Length > Constants.MaxMessageLengthBytes)
                    {
                        _logger.LogWarning("Oversized IPC message discarded ({Bytes} bytes)", line.Length);
                        continue;
                    }

                    IpcEnvelope? envelope;
                    try { envelope = JsonSerializer.Deserialize<IpcEnvelope>(line); }
                    catch (JsonException) { continue; }

                    if (envelope is null || !IpcProtocolVersion.IsCompatible(envelope.Version)) continue;

                    if (MessageReceived is not null)
                        await MessageReceived(envelope, SendAsync);
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

    private NamedPipeServerStream CreatePipe()
    {
        try
        {
            return CreateSecurePipe();
        }
        catch (Exception ex)
        {
            // ACL pipe creation can fail in some host contexts; fall back so IPC still works.
            _logger.LogWarning(ex, "Secure pipe ACL create failed — using default ACL pipe");
            return new NamedPipeServerStream(
                Constants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
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
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
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
