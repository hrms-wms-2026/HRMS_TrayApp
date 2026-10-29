namespace ONEVO.Agent.Service.IPC;

using System.Security.Cryptography;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

public sealed class NamedPipeAuthenticator
{
    /// <summary>
    /// Runs the nonce handshake on the connection's own reader/writer so that any message the
    /// client sends immediately after its response stays in the reader used by the message loop.
    /// </summary>
    public async Task<bool> AuthenticateAsync(StreamReader reader, StreamWriter writer, CancellationToken ct)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(Constants.NonceLengthBytes));

        var challenge = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceChallenge,
            Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload(nonce))
        };
        await writer.WriteLineAsync(IpcLineCodec.Serialize(challenge));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Constants.IpcConnectionTimeoutMs);

        string? line;
        try
        {
            line = await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // handshake timed out
        }

        if (!IpcLineCodec.TryParse(line, out var response)
            || response!.Type != IpcMessageTypes.NonceResponse)
            return false;

        NonceResponsePayload? payload;
        try { payload = response.Payload?.Deserialize<NonceResponsePayload>(); }
        catch (JsonException) { return false; }

        return payload?.Nonce == nonce;
    }
}
