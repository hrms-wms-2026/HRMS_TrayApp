// Drives the real ONEVO Agent IPC protocol (same wire format as ONEVO.Agent.TrayApp's
// NamedPipeClient) to exercise lifecycle actions against the live Service, without needing
// a rendered UI. Mirrors NamedPipeClient.cs's architecture: a single continuous background
// read loop plus a correlation-id -> TaskCompletionSource map. A synchronous "write, then
// read-until-match" pattern (the first version of this probe) stalls the pipe: the Service
// sends StatusResponse *and* an unsolicited PolicyPush per StatusRequest, and if the client
// stops reading after its first match, the server's second write blocks waiting for a
// reader while the client's next write blocks waiting for the server to read — a mutual
// pipe-buffer deadlock. Continuous draining (like the real client) avoids it entirely.
// Usage: dotnet run --no-build -- [status|clockin|startbreak|endbreak|clockout]
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

using var pipe = new NamedPipeClientStream(".", Constants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
Console.WriteLine("Connecting...");
using (var connectCts = new CancellationTokenSource(5000))
    await pipe.ConnectAsync(connectCts.Token);
Console.WriteLine("Connected.");

// --- Auth handshake ---
{
    using var authReader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
    await using var authWriter = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
    using var authCts = new CancellationTokenSource(5000);
    var challengeLine = await authReader.ReadLineAsync(authCts.Token) ?? throw new Exception("no challenge");
    var challenge = JsonSerializer.Deserialize<IpcEnvelope>(challengeLine)!;
    var noncePayload = challenge.Payload!.Value.Deserialize<NonceChallengePayload>()!;
    await authWriter.WriteLineAsync(JsonSerializer.Serialize(new IpcEnvelope
    {
        Type = IpcMessageTypes.NonceResponse,
        CorrelationId = challenge.CorrelationId,
        Payload = JsonSerializer.SerializeToElement(new NonceResponsePayload(noncePayload.Nonce))
    }));
    await authWriter.FlushAsync();
}
Console.WriteLine("Authenticated.");

var writer = new StreamWriter(pipe, utf8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
var reader = new StreamReader(pipe, utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
var pending = new ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>>();
using var lifeCts = new CancellationTokenSource();

// Continuous background drain — never stops reading, exactly like NamedPipeClient.ReadLoopAsync.
var readLoop = Task.Run(async () =>
{
    try
    {
        while (pipe.IsConnected && !lifeCts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(lifeCts.Token);
            if (line is null) break;
            var env = JsonSerializer.Deserialize<IpcEnvelope>(line);
            if (env is null) continue;
            if (!string.IsNullOrEmpty(env.CorrelationId) && pending.TryRemove(env.CorrelationId, out var tcs))
                tcs.TrySetResult(env);
            else
                Console.WriteLine($"  (unsolicited: {env.Type})");
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { Console.WriteLine($"  (read loop ended: {ex.Message})"); }
});

async Task<IpcEnvelope> SendAndWait(string type, object? payload, int timeoutMs)
{
    var cid = Guid.NewGuid().ToString("N");
    var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    pending[cid] = tcs;
    await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcEnvelope
    {
        Type = type,
        CorrelationId = cid,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload)
    }).AsMemory());
    using var cts = new CancellationTokenSource(timeoutMs);
    await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
    try { return await tcs.Task; }
    catch (OperationCanceledException) { throw new Exception($"TIMEOUT waiting for reply to {type} (cid={cid})"); }
}

void PrintLifecycle(string label, IpcEnvelope env)
{
    var r = env.Payload?.Deserialize<LifecycleResultPayload>();
    Console.WriteLine($"{label}: Success={r?.Success} State={r?.State} Error={r?.ErrorCode} Msg={r?.Message}");
    if (r?.Session is { } s)
        Console.WriteLine($"   Session: ClockInAt={s.ClockInAt:t} IsOnBreak={s.IsOnBreak} AccumulatedBreak={s.AccumulatedBreak} AccumulatedWork={s.AccumulatedWork} BreakCount={s.BreakSessionCount}");
}

var status0 = await SendAndWait(IpcMessageTypes.StatusRequest, null, 8000);
var status0Payload = status0.Payload?.Deserialize<StatusResponsePayload>();
Console.WriteLine($"Current state: {status0Payload?.State}");
if (status0Payload?.Session is { } sess0)
    Console.WriteLine($"   Session: ClockInAt={sess0.ClockInAt:t} IsOnBreak={sess0.IsOnBreak} AccumulatedBreak={sess0.AccumulatedBreak} AccumulatedWork={sess0.AccumulatedWork} BreakCount={sess0.BreakSessionCount}");

var action = cmd switch
{
    "clockin"    => LifecycleAction.ClockIn,
    "startbreak" => LifecycleAction.StartBreak,
    "endbreak"   => LifecycleAction.EndBreak,
    "clockout"   => LifecycleAction.ClockOut,
    _ => (LifecycleAction?)null
};

if (action is { } a)
{
    Console.WriteLine();
    Console.WriteLine($"=== {a} ===");
    PrintLifecycle(a.ToString(), await SendAndWait(IpcMessageTypes.LifecycleCommand, new LifecycleCommandPayload(a), 8000));
}

lifeCts.Cancel();
Console.WriteLine();
Console.WriteLine("Done.");
