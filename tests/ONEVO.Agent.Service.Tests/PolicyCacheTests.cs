using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

public class PolicyCacheTests
{
    [Fact]
    public void Default_enables_activity_signal_and_screenshots()
    {
        var cache = new PolicyCache();
        Assert.True(cache.Current.ActivitySignalEnabled);
        Assert.True(cache.Current.ScreenshotEnabled);
    }

    [Fact]
    public void Set_NotYetExpired_ReturnsPolicyAsStored()
    {
        var cache = new PolicyCache();
        cache.Set(new AgentPolicy
        {
            Version = "v1",
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });

        var current = cache.Current;
        Assert.True(current.ScreenshotEnabled);
        Assert.True(current.InactivityScreenshotEnabled);
        Assert.True(current.CameraVerificationEnabled);
    }

    [Fact]
    public void Set_Expired_DegradesCaptureFlagsToFalse()
    {
        var cache = new PolicyCache();
        cache.Set(new AgentPolicy
        {
            Version = "v1",
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var current = cache.Current;
        Assert.False(current.ScreenshotEnabled);
        Assert.False(current.InactivityScreenshotEnabled);
        Assert.False(current.CameraVerificationEnabled);
        // Version identity is preserved even while degraded — callers still know
        // which policy is cached; only the capture-affecting flags are suppressed.
        Assert.Equal("v1", current.Version);
    }
}
