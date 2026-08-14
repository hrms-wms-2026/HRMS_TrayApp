using ONEVO.Agent.Service.Buffer;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Buffer;

public class EvidenceSpoolStoreTests : IDisposable
{
    private readonly string _dir;

    public EvidenceSpoolStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"onevo-spool-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }

    [Fact]
    public void WriteReadRoundtrip_PreservesBytes()
    {
        var store = new EvidenceSpoolStore(_dir);
        var attemptId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        var path = store.Write(attemptId, bytes);
        var read = store.Read(path, attemptId);

        Assert.Equal(bytes, read);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var store = new EvidenceSpoolStore(_dir);
        var attemptId = Guid.NewGuid();
        var path = store.Write(attemptId, new byte[] { 9, 8, 7 });
        store.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HasCapacityFor_RespectsQuota()
    {
        var store = new EvidenceSpoolStore(_dir);
        Assert.True(store.HasCapacityFor(1024));
        Assert.False(store.HasCapacityFor((int)EvidenceSpoolStore.MaxQuotaBytes + 1));
    }

    [Fact]
    public void PurgeExpired_DeletesOldFiles()
    {
        var store = new EvidenceSpoolStore(_dir);
        var attemptId = Guid.NewGuid();
        var path = store.Write(attemptId, new byte[] { 1 });
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddHours(-80));

        store.PurgeExpired(DateTimeOffset.UtcNow);
        Assert.False(File.Exists(path));
    }
}
