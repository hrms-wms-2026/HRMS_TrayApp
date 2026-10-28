namespace ONEVO.Agent.Service.Security;

/// <summary>
/// Root of the agent's machine-wide data (%ProgramData%\ONEVO\Agent): credentials, device identity
/// and the evidence spool. The default location of every store that keeps data there comes from
/// <see cref="Root"/>.
/// </summary>
public static class AgentDataPaths
{
    private static readonly string ProductionRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONEVO", "Agent");

    private static string? _rootOverride;

    public static string Root => _rootOverride ?? ProductionRoot;

    /// <summary>
    /// Test assemblies only: redirects <see cref="Root"/> before any store is created, so a test can
    /// never overwrite or delete a real enrolled agent's files (AUD-017). Never set in production.
    /// </summary>
    internal static void OverrideRootForTests(string root) => _rootOverride = root;
}
