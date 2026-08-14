namespace ONEVO.Agent.Service.Buffer;

using System.Security.Cryptography;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Validates and reassembles chunked evidence transfers from the Tray App.
/// Caps concurrent transfers at two, 10 MB per transfer, two-minute assembly lifetime.
/// Never logs chunk content.
/// </summary>
public sealed class EvidenceTransferAssembler
{
    public const int MaxConcurrentTransfers = 2;
    public static readonly TimeSpan AssemblyLifetime = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, TransferState> _transfers = new();

    public void PurgeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _transfers
                .Where(kv => now - kv.Value.StartedAt > AssemblyLifetime)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in expired)
                _transfers.Remove(id);
        }
    }

    public EvidenceTransferStepResult HandleStart(
        EvidenceTransferStartPayload start,
        DateTimeOffset now)
    {
        PurgeExpired(now);

        if (start.TotalBytes < 0 || start.ChunkCount < 0)
            return Reject(start.Attempt.AttemptId, "invalid_start");

        if (start.TotalBytes > Constants.MaxScreenshotBytes)
            return Reject(start.Attempt.AttemptId, "transfer_too_large");

        if (start.ChunkCount > 0 && start.TotalBytes == 0)
            return Reject(start.Attempt.AttemptId, "invalid_start");

        var expectedChunks = start.TotalBytes == 0
            ? 0
            : (int)Math.Ceiling(start.TotalBytes / (double)Constants.EvidenceChunkSizeBytes);
        if (start.ChunkCount != expectedChunks)
            return Reject(start.Attempt.AttemptId, "chunk_count_mismatch");

        lock (_gate)
        {
            if (_transfers.ContainsKey(start.Attempt.AttemptId))
                return Reject(start.Attempt.AttemptId, "duplicate_transfer");

            if (_transfers.Count >= MaxConcurrentTransfers)
                return Reject(start.Attempt.AttemptId, "too_many_transfers");

            _transfers[start.Attempt.AttemptId] = new TransferState(
                start.Attempt,
                start.TotalBytes,
                start.ChunkCount,
                now);
        }

        return EvidenceTransferStepResult.Ok(start.Attempt.AttemptId);
    }

    public EvidenceTransferStepResult HandleChunk(
        EvidenceTransferChunkPayload chunk,
        DateTimeOffset now)
    {
        PurgeExpired(now);

        lock (_gate)
        {
            if (!_transfers.TryGetValue(chunk.AttemptId, out var state))
                return Reject(chunk.AttemptId, "unknown_transfer");

            if (chunk.Index < 0 || chunk.Index >= state.ExpectedChunkCount)
                return Reject(chunk.AttemptId, "invalid_chunk_index");

            if (state.ReceivedIndices.Contains(chunk.Index))
                return Reject(chunk.AttemptId, "duplicate_chunk");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(chunk.DataBase64);
            }
            catch (FormatException)
            {
                return Reject(chunk.AttemptId, "invalid_chunk_encoding");
            }

            if (bytes.Length == 0)
                return Reject(chunk.AttemptId, "empty_chunk");

            var expectedLength = Math.Min(
                Constants.EvidenceChunkSizeBytes,
                state.TotalBytes - (chunk.Index * Constants.EvidenceChunkSizeBytes));
            if (bytes.Length != expectedLength)
                return Reject(chunk.AttemptId, "chunk_size_mismatch");

            state.Chunks[chunk.Index] = bytes;
            state.ReceivedIndices.Add(chunk.Index);
            return EvidenceTransferStepResult.Ok(chunk.AttemptId);
        }
    }

    public EvidenceTransferCompleteResult TryComplete(Guid attemptId, DateTimeOffset now)
    {
        PurgeExpired(now);

        lock (_gate)
        {
            if (!_transfers.TryGetValue(attemptId, out var state))
                return EvidenceTransferCompleteResult.Rejected(attemptId, "unknown_transfer");

            _transfers.Remove(attemptId);

            if (state.ExpectedChunkCount == 0)
                return EvidenceTransferCompleteResult.Completed(attemptId, ReadOnlyMemory<byte>.Empty, state.Attempt);

            if (state.ReceivedIndices.Count != state.ExpectedChunkCount)
                return EvidenceTransferCompleteResult.Rejected(attemptId, "missing_chunks");

            for (var i = 0; i < state.ExpectedChunkCount; i++)
            {
                if (state.Chunks[i] is null)
                    return EvidenceTransferCompleteResult.Rejected(attemptId, "missing_chunks");
            }

            var assembled = state.Chunks.SelectMany(c => c).ToArray();
            if (assembled.Length != state.TotalBytes)
                return EvidenceTransferCompleteResult.Rejected(attemptId, "size_mismatch");

            if (state.Attempt.Outcome == InactivityCaptureOutcomes.Captured
                && !string.IsNullOrWhiteSpace(state.Attempt.Sha256))
            {
                var hash = Convert.ToHexString(SHA256.HashData(assembled)).ToLowerInvariant();
                if (!string.Equals(hash, state.Attempt.Sha256, StringComparison.OrdinalIgnoreCase))
                    return EvidenceTransferCompleteResult.Rejected(attemptId, "checksum_mismatch");
            }

            return EvidenceTransferCompleteResult.Completed(attemptId, assembled, state.Attempt);
        }
    }

    private static EvidenceTransferStepResult Reject(Guid attemptId, string code) =>
        EvidenceTransferStepResult.Fail(attemptId, code);

    private sealed class TransferState
    {
        public TransferState(
            InactivityCaptureAttemptPayload attempt,
            int totalBytes,
            int expectedChunkCount,
            DateTimeOffset startedAt)
        {
            Attempt = attempt;
            TotalBytes = totalBytes;
            ExpectedChunkCount = expectedChunkCount;
            StartedAt = startedAt;
            Chunks = new byte[expectedChunkCount][];
        }

        public InactivityCaptureAttemptPayload Attempt { get; }
        public int TotalBytes { get; }
        public int ExpectedChunkCount { get; }
        public DateTimeOffset StartedAt { get; }
        public byte[][] Chunks { get; }
        public HashSet<int> ReceivedIndices { get; } = [];
    }
}

public readonly record struct EvidenceTransferStepResult(
    Guid AttemptId,
    bool IsAccepted,
    string? ErrorCode)
{
    public static EvidenceTransferStepResult Ok(Guid attemptId) =>
        new(attemptId, true, null);

    public static EvidenceTransferStepResult Fail(Guid attemptId, string errorCode) =>
        new(attemptId, false, errorCode);
}

public readonly record struct EvidenceTransferCompleteResult(
    Guid AttemptId,
    bool Accepted,
    string? ErrorCode,
    ReadOnlyMemory<byte> JpegBytes,
    InactivityCaptureAttemptPayload? Attempt)
{
    public static EvidenceTransferCompleteResult Completed(
        Guid attemptId,
        ReadOnlyMemory<byte> jpegBytes,
        InactivityCaptureAttemptPayload attempt) =>
        new(attemptId, true, null, jpegBytes, attempt);

    public static EvidenceTransferCompleteResult Rejected(Guid attemptId, string errorCode) =>
        new(attemptId, false, errorCode, ReadOnlyMemory<byte>.Empty, null);
}
