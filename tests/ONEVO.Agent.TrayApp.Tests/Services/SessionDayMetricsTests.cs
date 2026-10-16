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

    [Fact]
    public void GetHourlyFocusFractions_EmptyWhenNoSamples()
    {
        var metrics = new SessionDayMetrics();
        Assert.Empty(metrics.GetHourlyFocusFractions());
    }

    [Fact]
    public void AddAppUsageSample_BucketsByHour_NormalizedAgainstBusiestHour()
    {
        var metrics = new SessionDayMetrics();
        var nineAm = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5.5));
        var tenAm = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(5.5));

        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(30), nineAm);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(15), nineAm);
        metrics.AddAppUsageSample("teams.exe", TimeSpan.FromMinutes(20), tenAm);

        var fractions = metrics.GetHourlyFocusFractions();

        Assert.Equal(2, fractions.Count);
        Assert.Equal(1.0, fractions[0]);
        Assert.Equal(20.0 / 45.0, fractions[1], precision: 6);
    }

    [Fact]
    public void GetHourlyFocusFractions_OnlyIncludesActiveHours_NoGapFilling()
    {
        var metrics = new SessionDayMetrics();
        var nineAm = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5.5));
        var twoPm = new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.FromHours(5.5));

        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(10), nineAm);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(5), twoPm);

        var fractions = metrics.GetHourlyFocusFractions();

        Assert.Equal(2, fractions.Count);
    }

    [Fact]
    public void ResetDay_ClearsHourlyFocusBuckets()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        metrics.ResetDay();
        Assert.Empty(metrics.GetHourlyFocusFractions());
    }

    [Fact]
    public void GetPeakFocusHour_ReturnsHourWithMostFocus_OrNullWhenEmpty()
    {
        var metrics = new SessionDayMetrics();
        Assert.Null(metrics.GetPeakFocusHour());

        var day = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(20), day.AddHours(9));
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(50), day.AddHours(14));
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(10), day.AddHours(16));

        Assert.Equal(14, metrics.GetPeakFocusHour());
    }

    private static readonly DateTimeOffset MondayClockIn = DateTimeOffset.Parse("2026-10-12T09:00:00+05:30");
    private static readonly DateTimeOffset TuesdayClockIn = DateTimeOffset.Parse("2026-10-13T09:00:00+05:30");

    private static SessionDayMetrics MetricsWithMondayData()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(30), MondayClockIn.AddHours(1));
        metrics.AddIdleSample(TimeSpan.FromMinutes(5));
        metrics.AddAllowedScreenshot(Guid.NewGuid(), MondayClockIn.AddHours(2), new byte[] { 1 });
        return metrics;
    }

    [Fact]
    public void BeginSession_FirstCall_KeepsDataAlreadyCollected()
    {
        // Tray restarted mid-session: whatever it collected since launch belongs to this session.
        var metrics = MetricsWithMondayData();

        metrics.BeginSession(MondayClockIn);

        Assert.Single(metrics.GetTopApps());
        Assert.Equal(TimeSpan.FromMinutes(5), metrics.TotalIdle);
        Assert.Single(metrics.GetActivityChecks());
    }

    [Fact]
    public void BeginSession_SameClockIn_KeepsData()
    {
        var metrics = new SessionDayMetrics();
        metrics.BeginSession(MondayClockIn);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(30), MondayClockIn.AddHours(1));

        metrics.BeginSession(MondayClockIn); // repeated status broadcast for the same session

        Assert.Single(metrics.GetTopApps());
        Assert.NotEmpty(metrics.GetHourlyFocusFractions());
    }

    [Fact]
    public void BeginSession_NewClockIn_ClearsPreviousSessionData()
    {
        var metrics = new SessionDayMetrics();
        metrics.BeginSession(MondayClockIn);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(30), MondayClockIn.AddHours(1));
        metrics.AddIdleSample(TimeSpan.FromMinutes(5));
        metrics.AddAllowedScreenshot(Guid.NewGuid(), MondayClockIn.AddHours(2), new byte[] { 1 });
        metrics.AddSkippedScreenshot(Guid.NewGuid(), MondayClockIn.AddHours(3));

        metrics.BeginSession(TuesdayClockIn);

        Assert.Empty(metrics.GetTopApps());
        Assert.Empty(metrics.GetHourlyFocusFractions());
        Assert.Null(metrics.GetPeakFocusHour());
        Assert.Equal(TimeSpan.Zero, metrics.TotalIdle);
        Assert.Empty(metrics.GetActivityChecks());
    }

    [Fact]
    public void BeginSession_NewClockIn_KeepsLastCompletedSession()
    {
        // App.xaml.cs compares a rebroadcast Stopped status against LastCompletedSession to avoid
        // re-opening yesterday's summary, so starting a new session must not forget it.
        var metrics = new SessionDayMetrics();
        var monday = new ONEVO.Agent.Shared.IPC.SessionSnapshot(
            MondayClockIn, MondayClockIn.AddHours(8), false, null,
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(7), null, 1);
        metrics.BeginSession(MondayClockIn);
        metrics.RememberCompletedSession(monday);

        metrics.BeginSession(TuesdayClockIn);

        Assert.Same(monday, metrics.LastCompletedSession);
    }
}
