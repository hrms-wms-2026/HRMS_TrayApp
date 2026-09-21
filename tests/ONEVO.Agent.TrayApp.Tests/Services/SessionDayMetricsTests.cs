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

    [Fact]
    public void AddSkippedScreenshot_IsReturnedAsSkippedActivityCheck_NotAsAllowed()
    {
        var metrics = new SessionDayMetrics();
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var skippedAt = DateTimeOffset.Parse("2026-09-18T10:07:00Z");

        metrics.AddSkippedScreenshot(id, skippedAt);

        Assert.Empty(metrics.GetAllowedScreenshots());
        var note = Assert.Single(metrics.GetActivityChecks());
        Assert.Equal(id, note.AttemptId);
        Assert.Equal(skippedAt, note.CapturedAt);
        Assert.True(note.IsSkipped);
        Assert.Empty(note.JpegBytes);
    }

    [Fact]
    public void GetActivityChecks_KeepsAllowedAndSkippedInOrder()
    {
        var metrics = new SessionDayMetrics();
        var allowedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var skippedId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        metrics.AddAllowedScreenshot(allowedId, DateTimeOffset.Parse("2026-09-18T10:05:00Z"), new byte[] { 1 });
        metrics.AddSkippedScreenshot(skippedId, DateTimeOffset.Parse("2026-09-18T10:07:00Z"));

        var checks = metrics.GetActivityChecks();
        Assert.Equal(2, checks.Count);
        Assert.False(checks[0].IsSkipped);
        Assert.Equal(allowedId, checks[0].AttemptId);
        Assert.True(checks[1].IsSkipped);
        Assert.Equal(skippedId, checks[1].AttemptId);
        Assert.Single(metrics.GetAllowedScreenshots());
    }

    [Fact]
    public void ResetDay_ClearsSkippedActivityChecks()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddSkippedScreenshot(Guid.NewGuid(), DateTimeOffset.UtcNow);
        metrics.ResetDay();
        Assert.Empty(metrics.GetActivityChecks());
    }
}
