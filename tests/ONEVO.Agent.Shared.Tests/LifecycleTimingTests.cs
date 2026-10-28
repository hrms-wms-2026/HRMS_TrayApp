namespace ONEVO.Agent.Shared.Tests;

using Xunit;

/// <summary>
/// AUD-008: the tray gave up on a lifecycle reply after 10 s while the Service's backend call had no
/// overall limit, so the employee saw "failed" while the clock action could still commit. The Service
/// must answer within its budget, and the tray must wait longer than that budget.
/// </summary>
public class LifecycleTimingTests
{
    [Fact]
    public void Tray_waits_longer_than_the_service_lifecycle_budget()
    {
        Assert.True(Constants.LifecycleReplyTimeout >= Constants.LifecycleCommandBudget + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Service_budget_allows_a_retry_after_one_slow_attempt()
    {
        Assert.True(Constants.LifecycleCommandBudget >= TimeSpan.FromSeconds(15));
    }
}
