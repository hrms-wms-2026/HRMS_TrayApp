using System.Runtime.CompilerServices;
using ONEVO.Agent.TrayApp.Services.Diagnostics;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests;

internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Tests must never open real browser tabs on the developer's machine.
        ConnectWorkspaceViewModel.OpenInBrowser = _ => { };

        // Nor append to the developer's real tray-boot.log: collectors and the coordinator trace through
        // TrayBootLog before any logger is attached (AUD-036).
        var bootLogDir = Path.Combine(Path.GetTempPath(), $"onevo-tray-tests-{Guid.NewGuid():N}");
        TrayBootLog.FallbackPath = Path.Combine(bootLogDir, "tray-boot.log");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(bootLogDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
