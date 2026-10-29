namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services.Diagnostics;

/// <summary>
/// AUD-036: code under test (collectors, coordinator) writes trace lines through TrayBootLog before any
/// logger is attached. Those must land in a temp file, never in the developer's real
/// %LOCALAPPDATA%\ONEVO\Agent\tray-boot.log (seen polluted by test runs on 2026-10-29).
/// </summary>
public sealed class TestPathIsolationTests
{
    [Fact]
    public void TrayBootLogFallback_InTests_IsNotTheRealLocalAppDataFile()
    {
        var realAgentDir = Path.GetDirectoryName(TrayBootLog.DefaultFallbackPath)!;

        Assert.NotEqual(TrayBootLog.DefaultFallbackPath, TrayBootLog.FallbackPath, StringComparer.OrdinalIgnoreCase);
        Assert.False(
            TrayBootLog.FallbackPath.StartsWith(realAgentDir, StringComparison.OrdinalIgnoreCase),
            $"Tests write the boot trace to {TrayBootLog.FallbackPath}");
    }
}
