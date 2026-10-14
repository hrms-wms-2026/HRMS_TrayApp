using System.Runtime.CompilerServices;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests;

internal static class TestAssemblySetup
{
    /// <summary>Tests must never open real browser tabs on the developer's machine.</summary>
    [ModuleInitializer]
    internal static void Initialize() => ConnectWorkspaceViewModel.OpenInBrowser = _ => { };
}
