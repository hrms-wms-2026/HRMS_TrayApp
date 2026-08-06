namespace ONEVO.Agent.Service.IPC;

using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;

public sealed class NamedPipeAuthenticator
{
    public async Task<bool> AuthenticateAsync(PipeStream pipe, CancellationToken ct)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(Constants.NonceLengthBytes));

        // No BOM — BOM breaks the peer's JSON parser on the first write.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var writer = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
        var challenge = new IpcEnvelope
        {
            Type = IpcMessageTypes.NonceChallenge,
            Payload = JsonSerializer.SerializeToElement(new NonceChallengePayload(nonce))
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(challenge));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Constants.IpcConnectionTimeoutMs);

        using var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var line = await reader.ReadLineAsync(cts.Token);
        if (line is null) return false;

        IpcEnvelope? response;
        try { response = JsonSerializer.Deserialize<IpcEnvelope>(line); }
        catch (JsonException) { return false; }

        if (response?.Type != IpcMessageTypes.NonceResponse) return false;

        NonceResponsePayload? payload;
        try { payload = response.Payload?.Deserialize<NonceResponsePayload>(); }
        catch (JsonException) { return false; }

        return payload?.Nonce == nonce;
    }
}
