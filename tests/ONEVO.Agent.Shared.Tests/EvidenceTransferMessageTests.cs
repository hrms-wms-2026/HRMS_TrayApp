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

    /// <summary>
    /// AUD-031: the backend's RequestSizeLimit(10 MiB) on /tray/screenshots and
    /// /tray/inactivity-attempts covers the whole multipart request, so a screenshot at the old
    /// 10 MiB client limit was refused with 413 every time.
    /// </summary>
    [Fact]
    public void Screenshot_limit_leaves_multipart_headroom_under_backend_request_limit()
    {
        const int backendRequestLimitBytes = 10 * 1024 * 1024;
        const int multipartHeadroomBytes = 256 * 1024;
        Assert.True(Constants.MaxScreenshotBytes + multipartHeadroomBytes <= backendRequestLimitBytes);
    }
}
