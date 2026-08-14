namespace ONEVO.Agent.TrayApp.Tests.Services;

using System.Linq;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed class EvidenceTransferClientTests
{
    private static InactivityCaptureAttemptPayload MakeAttempt(Guid id, string outcome) => new()
    {
        AttemptId = id,
        PolicyVersion = "v1",
        IdleStartedAt = DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
        PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z"),
        DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z"),
        CapturedAt = outcome == InactivityCaptureOutcomes.Captured
            ? DateTimeOffset.Parse("2026-08-10T01:05:04Z")
            : null,
        IdleDurationSeconds = 300,
        MonitorCount = outcome == InactivityCaptureOutcomes.Captured ? 2 : 0,
        Outcome = outcome
    };

    [Fact]
    public async Task LargePayload_SplitsIntoExpectedChunkSizes_InOrder()
    {
        var sent = new List<IpcEnvelope>();
        var client = new EvidenceTransferClient(
            send: (env, _) => { sent.Add(env); return Task.CompletedTask; },
            waitForAck: (id, _) => Task.FromResult<EvidenceTransferAckPayload?>(new EvidenceTransferAckPayload(id, true, null)));

        var bytes = new byte[65_537];
        new Random(42).NextBytes(bytes);
        var attemptId = Guid.NewGuid();

        var accepted = await client.SubmitAsync(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured), bytes, default);

        Assert.True(accepted);

        // Exact envelope type sequence: start -> chunk x3 -> complete.
        Assert.Equal(5, sent.Count);
        Assert.Equal(
        [
            IpcMessageTypes.EvidenceTransferStart,
            IpcMessageTypes.EvidenceTransferChunk,
            IpcMessageTypes.EvidenceTransferChunk,
            IpcMessageTypes.EvidenceTransferChunk,
            IpcMessageTypes.EvidenceTransferComplete
        ], sent.Select(e => e.Type));

        var start = sent[0].Payload!.Value.Deserialize<EvidenceTransferStartPayload>()!;
        Assert.Equal(65_537, start.TotalBytes);
        Assert.Equal(3, start.ChunkCount);

        var chunks = sent.Skip(1).Take(3)
            .Select(e => e.Payload!.Value.Deserialize<EvidenceTransferChunkPayload>()!)
            .ToArray();

        // 65,537 bytes -> 32,768 + 32,768 + 1, decoded (not encoded/base64) byte lengths.
        Assert.Equal([32_768, 32_768, 1], chunks.Select(c => Convert.FromBase64String(c.DataBase64).Length));
        Assert.Equal([0, 1, 2], chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.Equal(attemptId, c.AttemptId));

        // Reassembling the chunks reproduces the original bytes exactly.
        var reassembled = chunks.SelectMany(c => Convert.FromBase64String(c.DataBase64)).ToArray();
        Assert.Equal(bytes, reassembled);

        var complete = sent[4].Payload!.Value.Deserialize<EvidenceTransferCompletePayload>()!;
        Assert.Equal(attemptId, complete.AttemptId);
    }

    [Fact]
    public async Task MetadataOnlyAttempt_EmitsNoChunkEnvelopes()
    {
        var sent = new List<IpcEnvelope>();
        var client = new EvidenceTransferClient(
            send: (env, _) => { sent.Add(env); return Task.CompletedTask; },
            waitForAck: (id, _) => Task.FromResult<EvidenceTransferAckPayload?>(new EvidenceTransferAckPayload(id, true, null)));

        var accepted = await client.SubmitAsync(
            MakeAttempt(Guid.NewGuid(), InactivityCaptureOutcomes.Declined),
            ReadOnlyMemory<byte>.Empty,
            default);

        Assert.True(accepted);
        Assert.Equal(2, sent.Count); // start + complete only
        Assert.Equal(
        [
            IpcMessageTypes.EvidenceTransferStart,
            IpcMessageTypes.EvidenceTransferComplete
        ], sent.Select(e => e.Type));
        Assert.DoesNotContain(sent, e => e.Type == IpcMessageTypes.EvidenceTransferChunk);

        var start = sent[0].Payload!.Value.Deserialize<EvidenceTransferStartPayload>()!;
        Assert.Equal(0, start.TotalBytes);
        Assert.Equal(0, start.ChunkCount);
    }

    [Fact]
    public async Task ExactMultipleOfChunkSize_DoesNotEmitTrailingEmptyChunk()
    {
        var sent = new List<IpcEnvelope>();
        var client = new EvidenceTransferClient(
            send: (env, _) => { sent.Add(env); return Task.CompletedTask; },
            waitForAck: (id, _) => Task.FromResult<EvidenceTransferAckPayload?>(new EvidenceTransferAckPayload(id, true, null)));

        var bytes = new byte[Constants.EvidenceChunkSizeBytes * 2];

        await client.SubmitAsync(MakeAttempt(Guid.NewGuid(), InactivityCaptureOutcomes.Captured), bytes, default);

        var chunkCount = sent.Count(e => e.Type == IpcMessageTypes.EvidenceTransferChunk);
        Assert.Equal(2, chunkCount);
    }

    [Fact]
    public async Task RejectedAck_ReturnsFalse()
    {
        var client = new EvidenceTransferClient(
            send: (_, _) => Task.CompletedTask,
            waitForAck: (id, _) => Task.FromResult<EvidenceTransferAckPayload?>(
                new EvidenceTransferAckPayload(id, false, "policy_disabled")));

        var accepted = await client.SubmitAsync(
            MakeAttempt(Guid.NewGuid(), InactivityCaptureOutcomes.Declined), ReadOnlyMemory<byte>.Empty, default);

        Assert.False(accepted);
    }

    [Fact]
    public async Task NoAckReceived_ReturnsFalse()
    {
        var client = new EvidenceTransferClient(
            send: (_, _) => Task.CompletedTask,
            waitForAck: (_, _) => Task.FromResult<EvidenceTransferAckPayload?>(null));

        var accepted = await client.SubmitAsync(
            MakeAttempt(Guid.NewGuid(), InactivityCaptureOutcomes.Declined), ReadOnlyMemory<byte>.Empty, default);

        Assert.False(accepted);
    }
}
