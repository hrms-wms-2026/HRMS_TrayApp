namespace ONEVO.Agent.Service.Tests.Security;

using System.Runtime.CompilerServices;
using ONEVO.Agent.Service.Security;

/// <summary>
/// Runs when the test assembly loads, before any test: points the agent data root at a per-run
/// temp directory, so even a store constructed without a directory (parameterless, target-typed
/// <c>new()</c>, or a default inside a production type) can never touch the real
/// %ProgramData%\ONEVO\Agent credentials or identity (AUD-017).
/// </summary>
internal static class TestAgentDataRoot
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        var root = Path.Combine(Path.GetTempPath(), $"onevo-agent-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        AgentDataPaths.OverrideRootForTests(root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
