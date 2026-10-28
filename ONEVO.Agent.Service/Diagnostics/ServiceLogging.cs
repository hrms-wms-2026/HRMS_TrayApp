namespace ONEVO.Agent.Service.Diagnostics;

using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Shared.Diagnostics;

/// <summary>
/// The Service's persistent log (%ProgramData%\ONEVO\Agent\logs\service.log). Running as a Windows
/// Service it otherwise only reached EventLog, which drops scopes, so CorrelationId never showed
/// up anywhere (AUD-018). The folder gets the same restricted ACL as the rest of the agent data:
/// lines carry device/session ids and IPC client paths.
/// </summary>
public static class ServiceLogging
{
    public const string FileName = "service.log";

    public static string DefaultDirectory => Path.Combine(AgentDataPaths.Root, "logs");

    public static JsonFileLoggerProvider CreateFileProvider(string? directory = null)
    {
        var dir = directory ?? DefaultDirectory;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // Not elevated (e.g. a dev console run): the provider just drops lines it cannot write.
        }
        AgentDataAcl.TryRestrict(dir);
        return new JsonFileLoggerProvider(dir, FileName);
    }
}
