using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;

namespace ONEVO.Agent.TrayApp.Tests.Services;

public sealed class EmployeeSessionTests
{
    [Fact]
    public void ResolveWorkLocationDisplay_SavedValuePresent_ReturnsSavedValue()
    {
        Assert.Equal(
            "Chennai Office",
            EmployeeSession.ResolveWorkLocationDisplay("Chennai Office", policy: null));
    }

    [Fact]
    public void ResolveWorkLocationDisplay_EmptyAndNoPolicy_ReturnsDash()
    {
        Assert.Equal("—", EmployeeSession.ResolveWorkLocationDisplay(string.Empty, policy: null));
    }

    [Fact]
    public void ResolveWorkLocationDisplay_EmptyAndFixedOfficeWorkMode_ReturnsOffice()
    {
        var policy = new AgentPolicy
        {
            Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = false,
            ValidUntil = DateTimeOffset.UtcNow
        };

        Assert.Equal("Office", EmployeeSession.ResolveWorkLocationDisplay(string.Empty, policy));
    }

    [Fact]
    public void ResolveWorkLocationDisplay_EmptyAndFixedSelfRegisteredWorkMode_ReturnsWorkFromHome()
    {
        var policy = new AgentPolicy
        {
            Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = true,
            ValidUntil = DateTimeOffset.UtcNow
        };

        Assert.Equal("Work From Home", EmployeeSession.ResolveWorkLocationDisplay(string.Empty, policy));
    }

    [Fact]
    public void ResolveWorkLocationDisplay_EmptyAndDailyChoiceWorkMode_ReturnsDash()
    {
        // AllowsDailyLocationChoice employees who haven't confirmed today have no fixed location to
        // derive - the tray routes them to the picker before they ever reach a display like this.
        var policy = new AgentPolicy
        {
            Version = "v1", AllowsDailyLocationChoice = true, SelfRegistersLocation = false,
            ValidUntil = DateTimeOffset.UtcNow
        };

        Assert.Equal("—", EmployeeSession.ResolveWorkLocationDisplay(string.Empty, policy));
    }

    [Fact]
    public void WorkLocation_PreferenceSet_ReturnsPreferenceRegardlessOfPolicy()
    {
        var prefs = new FakePreferencesStore();
        prefs.Set(SessionPreferenceKeys.WorkLocationDisplay, "Other Approved Location");
        var policy = new AgentPolicy
        {
            Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = false,
            ValidUntil = DateTimeOffset.UtcNow
        };

        Assert.Equal("Other Approved Location", EmployeeSession.WorkLocation(prefs, policy));
    }

    [Fact]
    public void WorkLocation_PreferenceEmptyAndFixedOfficeWorkMode_DerivesOffice()
    {
        var prefs = new FakePreferencesStore();
        var policy = new AgentPolicy
        {
            Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = false,
            ValidUntil = DateTimeOffset.UtcNow
        };

        Assert.Equal("Office", EmployeeSession.WorkLocation(prefs, policy));
    }
}
