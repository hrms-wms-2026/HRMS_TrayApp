using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class SessionDayMetricsTests
{
    [Fact]
    public void AddAllowedScreenshot_IsReturnedByGetAllowedScreenshots()
    {
        var metrics = new SessionDayMetrics();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var capturedAt = DateTimeOffset.Parse("2026-09-18T10:05:00Z");

        metrics.AddAllowedScreenshot(id, capturedAt, new byte[] { 10, 20, 30 });

        var shot = Assert.Single(metrics.GetAllowedScreenshots());
        Assert.Equal(id, shot.AttemptId);
        Assert.Equal(capturedAt, shot.CapturedAt);
        Assert.Equal(new byte[] { 10, 20, 30 }, shot.JpegBytes);
    }

    [Fact]
    public void AddAllowedScreenshot_IgnoresEmptyJpeg()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddAllowedScreenshot(Guid.NewGuid(), DateTimeOffset.UtcNow, ReadOnlyMemory<byte>.Empty);
        Assert.Empty(metrics.GetAllowedScreenshots());
    }

    [Fact]
    public void AddAllowedScreenshot_KeepsMostRecentTwelve()
    {
        var metrics = new SessionDayMetrics();
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        metrics.AddAllowedScreenshot(first, DateTimeOffset.UtcNow, new byte[] { 1 });
        for (var i = 0; i < 12; i++)
            metrics.AddAllowedScreenshot(Guid.NewGuid(), DateTimeOffset.UtcNow, new byte[] { (byte)i });

        var shots = metrics.GetAllowedScreenshots();
        Assert.Equal(12, shots.Count);
        Assert.DoesNotContain(shots, s => s.AttemptId == first);
    }

    [Fact]
    public void ResetDay_ClearsAllowedScreenshots()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddAllowedScreenshot(Guid.NewGuid(), DateTimeOffset.UtcNow, new byte[] { 1 });
        metrics.ResetDay();
        Assert.Empty(metrics.GetAllowedScreenshots());
    }
}
