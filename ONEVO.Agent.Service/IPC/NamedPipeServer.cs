namespace ONEVO.Agent.Service.IPC;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
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

            Task SendAsync(IpcEnvelope envelope) =>
                writer.WriteLineAsync(JsonSerializer.Serialize(envelope));

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
}
