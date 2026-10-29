namespace ONEVO.Agent.Service.IPC;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

public sealed class NamedPipeServer : IAsyncDisposable
{
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly NamedPipeAuthenticator _authenticator;
    private readonly Func<NamedPipeServerStream> _createPipe;
    private CancellationTokenSource? _cts;

    private static readonly TimeSpan PipeCreateRetryDelay = TimeSpan.FromSeconds(1);

    public event Func<IpcEnvelope, Func<IpcEnvelope, Task>, Task>? MessageReceived;

    public NamedPipeServer(ILogger<NamedPipeServer> logger, NamedPipeAuthenticator authenticator)
        : this(logger, authenticator, CreateSecurePipe)
    {
    }

    internal NamedPipeServer(
        ILogger<NamedPipeServer> logger,
        NamedPipeAuthenticator authenticator,
        Func<NamedPipeServerStream> createPipe)
    {
        _logger = logger;
        _authenticator = authenticator;
        _createPipe = createPipe;
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
                // Creation is inside the guard: a single failure must not end the loop and
                // leave the service running without IPC (ISS-018).
                pipe = _createPipe();
                await pipe.WaitForConnectionAsync(ct);
                // Dispatch off the accept loop so the next instance is created immediately,
                // even when the handler's first steps complete synchronously.
                var connected = pipe;
                _ = Task.Run(() => HandleClientAsync(connected, ct), CancellationToken.None);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe accept failed");
                pipe?.Dispose();
                try { await Task.Delay(PipeCreateRetryDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            // One reader/writer for the whole connection: a separate handshake reader would
            // swallow messages the client sends right after authenticating (ISS-006).
            // The writer is intentionally not disposed: it owns nothing (leaveOpen) and its
            // dispose-time Flush throws IOException when the client has already disconnected.
            using var reader = IpcLineCodec.CreateReader(pipe);
            var writer = IpcLineCodec.CreateWriter(pipe);

            bool authenticated;
            try
            {
                authenticated = await _authenticator.AuthenticateAsync(reader, writer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation("IPC client dropped during handshake: {Message}", ex.Message);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!authenticated)
            {
                _logger.LogWarning("IPC client failed authentication — connection dropped");
                return;
            }

            _logger.LogInformation("IPC client authenticated");

            Task SendAsync(IpcEnvelope envelope) =>
                writer.WriteLineAsync(IpcLineCodec.Serialize(envelope));

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

                    if (!IpcLineCodec.TryParse(line, out var envelope)
                        || !IpcProtocolVersion.IsCompatible(envelope!.Version)) continue;

                    if (MessageReceived is not null)
                        await MessageReceived(envelope, SendAsync);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation("IPC client disconnected: {Message}", ex.Message);
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

    /// <summary>Idempotent: called by AgentWorker.StopAsync and again by the DI container (ISS-018).</summary>
    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }
}
