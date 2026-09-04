using System.Security.Cryptography;
using System.Text.Json;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;

using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Buffer;

[Collection(CredentialStoreFileCollection.Name)]
public class EvidenceTransferAssemblerTests
{
    private static InactivityCaptureAttemptPayload MakeAttempt(Guid id, string outcome, string? sha256 = null) => new()
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
        Outcome = outcome,
        Sha256 = sha256
    };

    [Fact]
    public void ValidThreeChunkTransfer_ReassemblesBytes()
    {
        var assembler = new EvidenceTransferAssembler();
        var now = DateTimeOffset.UtcNow;
        var attemptId = Guid.NewGuid();
        var bytes = new byte[65_537];
        Random.Shared.NextBytes(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured, hash),
            bytes.Length,
            3);

        Assert.True(assembler.HandleStart(start, now).IsAccepted);

        for (var i = 0; i < 3; i++)
        {
            var offset = i * Constants.EvidenceChunkSizeBytes;
            var length = Math.Min(Constants.EvidenceChunkSizeBytes, bytes.Length - offset);
            var chunkBytes = bytes.AsSpan(offset, length).ToArray();
            var chunk = new EvidenceTransferChunkPayload(
                attemptId, i, Convert.ToBase64String(chunkBytes));
            Assert.True(assembler.HandleChunk(chunk, now).IsAccepted);
        }

        var complete = assembler.TryComplete(attemptId, now);
        Assert.True(complete.Accepted);
        Assert.Equal(bytes, complete.JpegBytes.ToArray());
    }

    [Fact]
    public void MetadataOnly_CompletesWithEmptyBytes()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Declined),
            0,
            0);

        Assert.True(assembler.HandleStart(start, now).IsAccepted);
        var complete = assembler.TryComplete(attemptId, now);
        Assert.True(complete.Accepted);
        Assert.True(complete.JpegBytes.IsEmpty);
    }

    [Fact]
    public void MissingChunk_RejectsComplete()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var bytes = new byte[Constants.EvidenceChunkSizeBytes * 2];
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            bytes.Length,
            2);
        Assert.True(assembler.HandleStart(start, now).IsAccepted);

        var complete = assembler.TryComplete(attemptId, now);
        Assert.False(complete.Accepted);
        Assert.Equal("missing_chunks", complete.ErrorCode);
    }

    [Fact]
    public void DuplicateChunk_RejectsSecondChunk()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var bytes = new byte[100];
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            bytes.Length,
            1);
        Assert.True(assembler.HandleStart(start, now).IsAccepted);

        var chunk = new EvidenceTransferChunkPayload(attemptId, 0, Convert.ToBase64String(bytes));
        Assert.True(assembler.HandleChunk(chunk, now).IsAccepted);
        var dup = assembler.HandleChunk(chunk, now);
        Assert.False(dup.IsAccepted);
        Assert.Equal("duplicate_chunk", dup.ErrorCode);
    }

    [Fact]
    public void OutOfOrderIndex_RejectsChunk()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            Constants.EvidenceChunkSizeBytes * 2,
            2);
        Assert.True(assembler.HandleStart(start, now).IsAccepted);

        var bad = new EvidenceTransferChunkPayload(
            attemptId, 2, Convert.ToBase64String(new byte[1]));
        Assert.False(assembler.HandleChunk(bad, now).IsAccepted);
    }

    [Fact]
    public void OversizeTransfer_RejectsStart()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured),
            Constants.MaxScreenshotBytes + 1,
            1);
        var result = assembler.HandleStart(start, DateTimeOffset.UtcNow);
        Assert.False(result.IsAccepted);
        Assert.Equal("transfer_too_large", result.ErrorCode);
    }

    [Fact]
    public void ChecksumMismatch_RejectsComplete()
    {
        var assembler = new EvidenceTransferAssembler();
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var bytes = new byte[128];
        var start = new EvidenceTransferStartPayload(
            MakeAttempt(attemptId, InactivityCaptureOutcomes.Captured, "deadbeef"),
            bytes.Length,
            1);
        Assert.True(assembler.HandleStart(start, now).IsAccepted);
        Assert.True(assembler.HandleChunk(
            new EvidenceTransferChunkPayload(attemptId, 0, Convert.ToBase64String(bytes)), now).IsAccepted);

        var complete = assembler.TryComplete(attemptId, now);
        Assert.False(complete.Accepted);
        Assert.Equal("checksum_mismatch", complete.ErrorCode);
    }

    [Fact]
    public void ConcurrentTransferLimit_RejectsThirdStart()
    {
        var assembler = new EvidenceTransferAssembler();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < EvidenceTransferAssembler.MaxConcurrentTransfers; i++)
        {
            var id = Guid.NewGuid();
            var start = new EvidenceTransferStartPayload(
                MakeAttempt(id, InactivityCaptureOutcomes.Declined), 0, 0);
            Assert.True(assembler.HandleStart(start, now).IsAccepted);
        }

        var rejected = assembler.HandleStart(
            new EvidenceTransferStartPayload(
                MakeAttempt(Guid.NewGuid(), InactivityCaptureOutcomes.Declined), 0, 0),
            now);
        Assert.False(rejected.IsAccepted);
        Assert.Equal("too_many_transfers", rejected.ErrorCode);
    }
}

public class InactivityEvidenceHandlerTests
{
    [Fact]
    public void PolicyDisabled_RejectsStart()
    {
        var handler = BuildHandler(inactivityEnabled: false);
        var attemptId = Guid.NewGuid();
        var ack = handler.HandleStart(
            new EvidenceTransferStartPayload(
                new InactivityCaptureAttemptPayload
                {
                    AttemptId = attemptId,
                    PolicyVersion = "v1",
                    IdleStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    PromptedAt = DateTimeOffset.UtcNow,
                    IdleDurationSeconds = 300,
                    MonitorCount = 0,
                    Outcome = InactivityCaptureOutcomes.Declined
                },
                0,
                0),
            DateTimeOffset.UtcNow);

        Assert.False(ack.Accepted);
        Assert.Equal("inactivity_screenshot_disabled", ack.ErrorCode);
    }

    [Fact]
    public void Complete_PersistsMetadataOnlyAttempt()
    {
        var buffer = ActivityRecordBuffer.CreateInMemory();
        var handler = BuildHandler(buffer: buffer);
        var attemptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var attempt = new InactivityCaptureAttemptPayload
        {
            AttemptId = attemptId,
            PolicyVersion = "v1",
            IdleStartedAt = now.AddMinutes(-5),
            PromptedAt = now,
            IdleDurationSeconds = 300,
            MonitorCount = 0,
            Outcome = InactivityCaptureOutcomes.Declined
        };

        Assert.True(handler.HandleStart(new EvidenceTransferStartPayload(attempt, 0, 0), now).Accepted);
        var ack = handler.HandleComplete(attemptId, now);
        Assert.True(ack.Accepted);
        Assert.Equal(1, buffer.Count);
    }

    private static InactivityEvidenceHandler BuildHandler(
        bool inactivityEnabled = true,
        ActivityRecordBuffer? buffer = null)
    {
        var policy = new PolicyCache();
        policy.Set(new AgentPolicy
        {
            Version = "test",
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = inactivityEnabled,
            CameraVerificationEnabled = false,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });

        var state = new AgentStateMachine();
        Assert.True(state.TryTransition(MonitoringState.Stopped, out _));
        Assert.True(state.TryTransition(MonitoringState.Active, out _));

        return new InactivityEvidenceHandler(
            new EvidenceTransferAssembler(),
            buffer ?? ActivityRecordBuffer.CreateInMemory(),
            new EvidenceSpoolStore(Path.Combine(Path.GetTempPath(), $"onevo-spool-{Guid.NewGuid():N}")),
            new DpapiEvidenceProtector(),
            state,
            policy,
            new DeviceIdentityStore(),
            NullLogger<InactivityEvidenceHandler>.Instance);
    }
}
