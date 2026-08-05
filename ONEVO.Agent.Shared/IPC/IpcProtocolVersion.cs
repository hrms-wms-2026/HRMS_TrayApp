namespace ONEVO.Agent.Shared.IPC;

public static class IpcProtocolVersion
{
    public const string Current = "1.0";

    public static bool IsCompatible(string? version) =>
        version == Current;
}
