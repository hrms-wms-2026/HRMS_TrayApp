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
            var pipe = CreateSecurePipe();
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch (Exception ex) { _logger.LogError(ex, "Pipe accept failed"); pipe.Dispose(); }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            // One reader/writer for the whole connection: a separate handshake reader would
            // swallow messages the client sends right after authenticating (ISS-006).
            using var reader = IpcLineCodec.CreateReader(pipe);
            await using var writer = IpcLineCodec.CreateWriter(pipe);

            if (!await _authenticator.AuthenticateAsync(reader, writer, ct))
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

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
    }
}
