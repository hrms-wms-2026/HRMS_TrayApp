namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class WindowsInactivityPromptServiceTests
{
    [Theory]
    [InlineData(120, "No keyboard or mouse activity was detected for 2 minutes. Allow a screenshot of all connected monitors?")]
    [InlineData(600, "No keyboard or mouse activity was detected for 10 minutes. Allow a screenshot of all connected monitors?")]
    [InlineData(121, "No keyboard or mouse activity was detected for 2 minutes. Allow a screenshot of all connected monitors?")]
    public void BuildNotificationBody_UsesActualIdleMinutes(int idleSeconds, string expected)
    {
        var actual = WindowsInactivityPromptService.BuildNotificationBody(TimeSpan.FromSeconds(idleSeconds));

        Assert.Equal(expected, actual);
    }
}
