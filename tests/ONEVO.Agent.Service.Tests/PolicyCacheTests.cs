using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Shared.Models;
using Xunit;

namespace ONEVO.Agent.Service.Tests;

public class PolicyCacheTests
{
    [Fact]
    public void Default_disables_monitoring_until_server_policy_is_available()
    {
        var cache = new PolicyCache();
        Assert.False(cache.Current.LocationTrackingEnabled);
        Assert.False(cache.Current.ActivitySignalEnabled);
        Assert.False(cache.Current.AppUsageEnabled);
        Assert.False(cache.Current.ScreenshotEnabled);
        Assert.False(cache.Current.InactivityScreenshotEnabled);
        Assert.False(cache.Current.CameraVerificationEnabled);
        // "none" — no server policy is in effect, so there is no active scope to report; must
        // not default to "employee" (which would misleadingly imply a live authorized policy).
        Assert.Equal("none", cache.Current.EffectiveScope);
    }

    [Fact]
    public void CreateDefault_IsAllFalse_RegardlessOfBuildConfiguration()
    {
        // PolicyCache.CreateDefault() has no #if DEBUG branch — this is the single source of
        // truth for the fail-closed default in every build configuration, Debug or Release.
        // Asserted explicitly so a future edit can't quietly carve out a "local dev only" path
        // that enables monitoring outside of a real server-authoritative policy.
        var policy = PolicyCache.CreateDefault();

        Assert.False(policy.LocationTrackingEnabled);
        Assert.False(policy.ActivitySignalEnabled);
        Assert.False(policy.AppUsageEnabled);
        Assert.False(policy.ScreenshotEnabled);
        Assert.False(policy.InactivityScreenshotEnabled);
        Assert.False(policy.CameraVerificationEnabled);
        Assert.Equal("none", policy.EffectiveScope);
        Assert.Equal("server-policy-unavailable", policy.Version);
        Assert.Equal(DateTimeOffset.MinValue, policy.ValidUntil);
    }

    [Fact]
    public void Set_NotYetExpired_ReturnsPolicyAsStored()
    {
        var cache = new PolicyCache();
        cache.Set(new AgentPolicy
        {
            Version = "v1",
            LocationTrackingEnabled = true,
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        });

        var current = cache.Current;
        Assert.True(current.LocationTrackingEnabled);
        Assert.True(current.ScreenshotEnabled);
        Assert.True(current.InactivityScreenshotEnabled);
        Assert.True(current.CameraVerificationEnabled);
    }

    [Fact]
    public void Set_Expired_DisablesAllMonitoring()
    {
        var cache = new PolicyCache();
        cache.Set(new AgentPolicy
        {
            Version = "v1",
            LocationTrackingEnabled = true,
            ActivitySignalEnabled = true,
            AppUsageEnabled = true,
            ScreenshotEnabled = true,
            InactivityScreenshotEnabled = true,
            CameraVerificationEnabled = true,
            ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var current = cache.Current;
        Assert.False(current.LocationTrackingEnabled);
        Assert.False(current.ActivitySignalEnabled);
        Assert.False(current.AppUsageEnabled);
        Assert.False(current.ScreenshotEnabled);
        Assert.False(current.InactivityScreenshotEnabled);
        Assert.False(current.CameraVerificationEnabled);
        Assert.Equal("server-policy-unavailable", current.Version);
        Assert.Equal("none", current.EffectiveScope);
    }
}
