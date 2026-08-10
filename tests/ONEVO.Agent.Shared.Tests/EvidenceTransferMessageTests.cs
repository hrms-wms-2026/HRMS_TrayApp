namespace ONEVO.Agent.Shared.Tests;

using Xunit;

public class EvidenceTransferMessageTests
{
    [Fact]
    public void Chunk_limit_fits_existing_ipc_envelope_limit()
    {
        var encodedCharacters = 4 * ((Constants.EvidenceChunkSizeBytes + 2) / 3);
        Assert.True(encodedCharacters + 8_192 < Constants.MaxMessageLengthBytes);
    }
}
