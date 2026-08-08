using ONEVO.Agent.Service.Policy;
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
}
